using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace CraftoraApi.Data;

public static class DatabaseBootstrapper
{
    public static async Task<DatabaseBootstrapResult> InitializeAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var migrations = dbContext.Database.GetMigrations().ToArray();
        if (migrations.Length == 0)
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            return new DatabaseBootstrapResult(
                CreatedFromCurrentModel: true,
                AppliedMigrationCount: 0);
        }

        var databaseCreator = dbContext.GetService<IRelationalDatabaseCreator>();
        if (!await databaseCreator.HasTablesAsync(cancellationToken))
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            await BaselineMigrationHistoryAsync(
                dbContext,
                migrations,
                cancellationToken);

            return new DatabaseBootstrapResult(
                CreatedFromCurrentModel: true,
                AppliedMigrationCount: migrations.Length);
        }

        var pendingMigrations = (await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken))
            .ToArray();

        if (pendingMigrations.Length > 0)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        return new DatabaseBootstrapResult(
            CreatedFromCurrentModel: false,
            AppliedMigrationCount: pendingMigrations.Length);
    }

    private static async Task BaselineMigrationHistoryAsync(
        AppDbContext dbContext,
        IReadOnlyCollection<string> migrations,
        CancellationToken cancellationToken)
    {
        var historyRepository = dbContext.GetService<IHistoryRepository>();
        var productVersion = ProductInfo.GetVersion();

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            historyRepository.GetCreateIfNotExistsScript(),
            cancellationToken);

        foreach (var migration in migrations)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                historyRepository.GetInsertScript(
                    new HistoryRow(migration, productVersion)),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed record DatabaseBootstrapResult(
    bool CreatedFromCurrentModel,
    int AppliedMigrationCount);
