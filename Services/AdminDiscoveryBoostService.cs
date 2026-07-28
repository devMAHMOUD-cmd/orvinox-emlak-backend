using System.Data;
using System.Data.Common;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Admin;
using CraftoraApi.Middleware;
using CraftoraApi.Redis;
using CraftoraApi.Services.Discovery;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CraftoraApi.Services;

public sealed class AdminDiscoveryBoostService : IAdminDiscoveryBoostService
{
    private readonly AppDbContext _dbContext;
    private readonly ICacheService _cacheService;

    public AdminDiscoveryBoostService(
        AppDbContext dbContext,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<AdminDiscoveryBoostDto> SetAsync(
        Guid adminUserId,
        AdminDiscoveryBoostRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var contentType = NormalizeContentType(request.ContentType);
        if (adminUserId == Guid.Empty ||
            request.ContentId == Guid.Empty ||
            request.CreditAmount is < 1 or > 100000)
        {
            throw new BadRequestException("Discovery boost istegi gecersiz.");
        }

        var startsAt = (request.StartsAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var endsAt = (request.EndsAt ?? startsAt.AddDays(7)).ToUniversalTime();
        if (endsAt <= startsAt || endsAt > startsAt.AddDays(30))
        {
            throw new BadRequestException(
                "Discovery boost suresi 30 gunden uzun olamaz.");
        }

        try
        {
            var result = await QuerySingleAsync(
                """
                SELECT
                    boost_id,
                    result_content_type,
                    result_content_id,
                    result_shop_id,
                    credit_total,
                    credit_remaining,
                    starts_at,
                    ends_at,
                    enabled
                FROM public.set_discovery_boost(
                    CAST(@admin_user_id AS uuid),
                    CAST(@content_type AS text),
                    CAST(@content_id AS uuid),
                    CAST(@credit_amount AS integer),
                    CAST(@starts_at AS timestamptz),
                    CAST(@ends_at AS timestamptz))
                """,
                command =>
                {
                    AddParameter(command, "admin_user_id", adminUserId);
                    AddParameter(command, "content_type", contentType);
                    AddParameter(command, "content_id", request.ContentId);
                    AddParameter(command, "credit_amount", request.CreditAmount);
                    AddParameter(command, "starts_at", startsAt);
                    AddParameter(command, "ends_at", endsAt);
                },
                reader => new AdminDiscoveryBoostDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    ContentTitle: null,
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    ReadDateTimeOffset(reader, 6),
                    ReadDateTimeOffset(reader, 7),
                    reader.GetBoolean(8),
                    UpdatedAt: DateTimeOffset.UtcNow),
                cancellationToken);

            await _cacheService.IncrementAsync(
                DiscoveryCacheKeys.BoostVersion,
                cancellationToken: cancellationToken);
            return result;
        }
        catch (PostgresException exception) when (exception.SqlState == "P0002")
        {
            throw new NotFoundException("Boost uygulanabilir discovery icerigi bulunamadi.");
        }
        catch (PostgresException exception) when (exception.SqlState == "23514")
        {
            throw new BadRequestException("Discovery boost istegi gecersiz.");
        }
    }

    public Task<IReadOnlyList<AdminDiscoveryBoostDto>> GetListAsync(
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        if (adminUserId == Guid.Empty)
        {
            throw new UnauthorizedException("Gecersiz kullanici token'i.");
        }

        return QueryListAsync(
            """
            SELECT
                boost_id,
                result_content_type,
                result_content_id,
                result_shop_id,
                content_title,
                credit_total,
                credit_remaining,
                starts_at,
                ends_at,
                enabled,
                updated_at
            FROM public.get_admin_discovery_boosts(CAST(@admin_user_id AS uuid))
            """,
            command => AddParameter(command, "admin_user_id", adminUserId),
            MapBoost,
            cancellationToken);
    }

    public async Task StopAsync(
        Guid adminUserId,
        Guid boostId,
        CancellationToken cancellationToken = default)
    {
        if (adminUserId == Guid.Empty || boostId == Guid.Empty)
        {
            throw new BadRequestException("Discovery boost kimligi gecersiz.");
        }

        var stopped = await ExecuteScalarAsync<bool>(
            """
            SELECT public.stop_discovery_boost(
                CAST(@admin_user_id AS uuid),
                CAST(@boost_id AS uuid))
            """,
            command =>
            {
                AddParameter(command, "admin_user_id", adminUserId);
                AddParameter(command, "boost_id", boostId);
            },
            cancellationToken);
        if (!stopped)
        {
            throw new NotFoundException("Aktif discovery boost bulunamadi.");
        }

        await _cacheService.IncrementAsync(
            DiscoveryCacheKeys.BoostVersion,
            cancellationToken: cancellationToken);
    }

    private async Task<T> QuerySingleAsync<T>(
        string sql,
        Action<DbCommand> configure,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var results = await QueryListAsync(
            sql,
            configure,
            map,
            cancellationToken);
        return results.Count == 1
            ? results[0]
            : throw new InvalidOperationException("Discovery boost function returned no result.");
    }

    private async Task<IReadOnlyList<T>> QueryListAsync<T>(
        string sql,
        Action<DbCommand> configure,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure(command);

            var results = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(map(reader));
            }

            return results;
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task<T> ExecuteScalarAsync<T>(
        string sql,
        Action<DbCommand> configure,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure(command);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is T result
                ? result
                : throw new InvalidOperationException(
                    "Discovery boost function returned an invalid result.");
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static AdminDiscoveryBoostDto MapBoost(DbDataReader reader)
    {
        return new AdminDiscoveryBoostDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            ReadDateTimeOffset(reader, 7),
            ReadDateTimeOffset(reader, 8),
            reader.GetBoolean(9),
            reader.IsDBNull(10)
                ? null
                : ReadDateTimeOffset(reader, 10));
    }

    private static DateTimeOffset ReadDateTimeOffset(
        DbDataReader reader,
        int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return new DateTimeOffset(
            DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static string NormalizeContentType(string? contentType)
    {
        return contentType?.Trim().ToLowerInvariant() switch
        {
            "media" => "media",
            "product" => "product",
            "course" => "course",
            _ => throw new BadRequestException(
                "Discovery boost contentType media, product veya course olmalidir.")
        };
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
