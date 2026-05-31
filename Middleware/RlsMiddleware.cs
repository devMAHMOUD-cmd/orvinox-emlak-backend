using System.Data;
using System.Security.Claims;
using CraftoraApi.Data;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Middleware;

public sealed class RlsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RlsMiddleware> _logger;

    public RlsMiddleware(RequestDelegate next, ILogger<RlsMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dbContext);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await _next(context);
            return;
        }

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(context.RequestAborted);
        }

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_user_id', {userId.ToString("D")}, false);",
                context.RequestAborted);

            await _next(context);
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync("RESET app.current_user_id;");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RLS current_user_id resetlenemedi");
                }
            }

            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }
}
