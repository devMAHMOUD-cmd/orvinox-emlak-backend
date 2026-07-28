using CraftoraApi.Middleware;
using CraftoraApi.Hubs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace CraftoraApi.Extensions;

public static class MiddlewareExtensions
{
    public static WebApplication UseCraftoraMiddleware(this WebApplication app)
    {
        var environment = app.Environment;
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        forwardedHeadersOptions.KnownNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();

        app.UseForwardedHeaders(forwardedHeadersOptions);

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, elapsed, exception) =>
            {
                if (httpContext.Request.Path.StartsWithSegments("/health"))
                {
                    return Serilog.Events.LogEventLevel.Debug;
                }

                if (exception is not null)
                {
                    return Serilog.Events.LogEventLevel.Error;
                }

                return elapsed > 1000
                    ? Serilog.Events.LogEventLevel.Warning
                    : Serilog.Events.LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set(
                    "RemoteIP",
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                diagnosticContext.Set(
                    "UserId",
                    httpContext.User.FindFirst("sub")?.Value ?? "anonymous");
                diagnosticContext.Set(
                    "RequestPath",
                    httpContext.Request.Path.ToString());
            };
        });

        app.UseExceptionMiddleware();

        app.Use(async (context, next) =>
        {
            if (string.IsNullOrEmpty(context.Request.Headers["X-Request-Id"]))
            {
                context.Request.Headers["X-Request-Id"] = Guid.NewGuid().ToString("N");
            }

            var startedAt = DateTime.UtcNow;

            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers.Remove("Server");
            context.Response.Headers["X-Request-Id"] = context.Request.Headers["X-Request-Id"];

            context.Response.OnStarting(() =>
            {
                var elapsedMilliseconds = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                context.Response.Headers["X-Response-Time"] = $"{elapsedMilliseconds}ms";
                return Task.CompletedTask;
            });

            await next();
        });

        app.UseStaticFiles();
        app.UseRouting();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseCors("CraftoraCorsPolicy");
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<RlsMiddleware>();
        app.UseAuthorization();

        if (environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Craftora API v1");
                options.RoutePrefix = "api-docs";
                options.DefaultModelsExpandDepth(2);
                options.DefaultModelExpandDepth(-1);
            });
        }

        app.MapHealthChecks(
                "/health",
                new HealthCheckOptions
                {
                    ResponseWriter = environment.IsDevelopment()
                        ? WriteHealthCheckResponseAsync
                        : WritePublicHealthCheckResponseAsync
                })
            .DisableRateLimiting();

        if (environment.IsDevelopment())
        {
            app.MapHealthChecks(
                    "/health/detailed",
                    new HealthCheckOptions
                    {
                        Predicate = _ => true,
                        ResponseWriter = WriteHealthCheckResponseAsync
                    })
                .DisableRateLimiting();
        }

        app.MapControllers();
        app.MapHub<NotificationHub>("/hubs/notifications");

        return app;
    }

    private static async Task WriteHealthCheckResponseAsync(
        HttpContext context,
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration.TotalMilliseconds,
                exception = entry.Value.Exception?.Message,
                data = entry.Value.Data
            })
        };

        await context.Response.WriteAsJsonAsync(response);
    }

    private static async Task WritePublicHealthCheckResponseAsync(
        HttpContext context,
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                duration = entry.Value.Duration.TotalMilliseconds
            })
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}
