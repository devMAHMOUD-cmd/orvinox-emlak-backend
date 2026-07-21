using CraftoraApi.Services.Interfaces;

namespace CraftoraApi.HostedServices;

public sealed class WeeklySellerReportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeeklySellerReportWorker> _logger;

    public WeeklySellerReportWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<WeeklySellerReportWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;
            var nextRunUtc = GetNextSundayReportRunUtc(nowUtc);
            var delay = nextRunUtc - nowUtc;

            _logger.LogInformation(
                "Weekly seller report worker scheduled. NextRunUtc: {NextRunUtc}",
                nextRunUtc);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var (startUtc, endUtc) = GetCurrentReportWindowUtc(nextRunUtc);

                using var scope = _scopeFactory.CreateScope();
                var reportService = scope.ServiceProvider.GetRequiredService<IWeeklySellerReportService>();

                await reportService.QueueWeeklyReportsAsync(
                    startUtc,
                    endUtc,
                    cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Weekly seller report worker failed.");
            }
        }
    }

    private static DateTime GetNextSundayReportRunUtc(DateTime nowUtc)
    {
        var timeZone = GetIstanbulTimeZone();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        var targetLocal = nowLocal.Date.AddDays(DaysUntilSunday(nowLocal.DayOfWeek)).AddHours(20);

        if (targetLocal <= nowLocal)
        {
            targetLocal = targetLocal.AddDays(7);
        }

        return TimeZoneInfo.ConvertTimeToUtc(targetLocal, timeZone);
    }

    private static (DateTime StartUtc, DateTime EndUtc) GetCurrentReportWindowUtc(DateTime runUtc)
    {
        var timeZone = GetIstanbulTimeZone();
        var runLocal = TimeZoneInfo.ConvertTimeFromUtc(runUtc, timeZone);
        var daysSinceMonday = ((int)runLocal.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var mondayLocal = runLocal.Date.AddDays(-daysSinceMonday);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(mondayLocal, timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(runLocal, timeZone);

        return (startUtc, endUtc);
    }

    private static int DaysUntilSunday(DayOfWeek dayOfWeek)
    {
        return ((int)DayOfWeek.Sunday - (int)dayOfWeek + 7) % 7;
    }

    private static TimeZoneInfo GetIstanbulTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }
}
