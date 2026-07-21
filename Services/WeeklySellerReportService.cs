using CraftoraApi.Data;
using CraftoraApi.DTOs.Seller;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Services;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class WeeklySellerReportService : IWeeklySellerReportService
{
    private const string ReportBucketName = "private-products";
    private const string ReportFileName = "craftora-haftalik-rapor.pdf";
    private const int ReportDownloadExpiryMinutes = 60 * 24 * 7;

    private readonly AppDbContext _dbContext;
    private readonly IPdfService _pdfService;
    private readonly IStorageService _storageService;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ILogger<WeeklySellerReportService> _logger;

    public WeeklySellerReportService(
        AppDbContext dbContext,
        IPdfService pdfService,
        IStorageService storageService,
        IRabbitMqPublisher rabbitMqPublisher,
        ILogger<WeeklySellerReportService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task QueueWeeklyReportsAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? sellerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var sellers = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Shop)
                .ThenInclude(shop => shop!.SellerSubscription)
            .Include(user => user.SellerNotificationPreference)
            .Where(user =>
                user.IsActive == true &&
                user.DeletedAt == null &&
                user.Shop != null &&
                user.Shop.IsActive == true &&
                user.Shop.SellerSubscription != null &&
                user.Shop.SellerSubscription.Status == SubStatus.Active &&
                (user.SellerNotificationPreference == null ||
                    user.SellerNotificationPreference.WeeklyReportEmails))
            .Where(user => !sellerUserId.HasValue || user.Id == sellerUserId.Value)
            .ToListAsync(cancellationToken);

        foreach (var seller in sellers)
        {
            if (seller.Shop is null)
            {
                continue;
            }

            try
            {
                await GenerateAndQueueWeeklyReportForSellerAsync(
                    seller.Id,
                    seller.Shop.Id,
                    seller.Shop.ShopName,
                    seller.Email,
                    seller.FullName ?? seller.Email,
                    startDateUtc,
                    endDateUtc,
                    cancellationToken);

                _logger.LogInformation(
                    "Weekly seller report queued. SellerUserId: {SellerUserId}, ShopId: {ShopId}",
                    seller.Id,
                    seller.Shop.Id);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Weekly seller report failed. SellerUserId: {SellerUserId}, ShopId: {ShopId}",
                    seller.Id,
                    seller.Shop.Id);
            }
        }
    }

    public async Task<WeeklySellerReportPreviewResponseDto> GenerateAndQueueWeeklyReportAsync(
        Guid sellerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default)
    {
        var seller = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Shop)
                .ThenInclude(shop => shop!.SellerSubscription)
            .FirstOrDefaultAsync(
                user =>
                    user.Id == sellerUserId &&
                    user.IsActive == true &&
                    user.DeletedAt == null &&
                    user.Shop != null &&
                    user.Shop.IsActive == true &&
                    user.Shop.SellerSubscription != null &&
                    user.Shop.SellerSubscription.Status == SubStatus.Active,
                cancellationToken);

        if (seller is null || seller.Shop is null)
        {
            throw new NotFoundException("Aktif magaza bulunamadi.");
        }

        return await GenerateAndQueueWeeklyReportForSellerAsync(
            seller.Id,
            seller.Shop.Id,
            seller.Shop.ShopName,
            seller.Email,
            seller.FullName ?? seller.Email,
            startDateUtc,
            endDateUtc,
            cancellationToken);
    }

    private async Task<WeeklySellerReportPreviewResponseDto> GenerateAndQueueWeeklyReportForSellerAsync(
        Guid sellerUserId,
        Guid shopId,
        string shopName,
        string sellerEmail,
        string sellerName,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var reportData = await BuildReportDataAsync(
            sellerUserId,
            shopId,
            shopName,
            sellerEmail,
            sellerName,
            startDateUtc,
            endDateUtc,
            cancellationToken);

        var pdfBytes = await _pdfService.GenerateWeeklySellerReportPdfAsync(
            reportData,
            cancellationToken);
        var objectKey = $"seller-reports/{shopId:D}/{endDateUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}.pdf";

        await _storageService.UploadFileAsync(
            ReportBucketName,
            objectKey,
            pdfBytes,
            "application/pdf",
            cancellationToken);

        var reportUrl = _storageService.GeneratePresignedDownloadUrl(
            ReportBucketName,
            objectKey,
            ReportDownloadExpiryMinutes);
        var expiresAt = DateTime.UtcNow.AddMinutes(ReportDownloadExpiryMinutes);

        await _rabbitMqPublisher.PublishSendEmailCommand(
            new SendEmailCommand(
                sellerEmail,
                "Craftora haftalık mağaza raporun hazır",
                BuildEmailBody(reportData, reportUrl),
                true),
            cancellationToken);

        return new WeeklySellerReportPreviewResponseDto(
            "Haftalık rapor hazırlandı.",
            reportUrl,
            ReportFileName,
            expiresAt);
    }

    private async Task<WeeklySellerReportData> BuildReportDataAsync(
        Guid sellerUserId,
        Guid shopId,
        string shopName,
        string sellerEmail,
        string sellerName,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var events = _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item =>
                item.ShopId == shopId &&
                item.CreatedAt >= startDateUtc &&
                item.CreatedAt <= endDateUtc);

        var orders = _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.ShopId == shopId &&
                order.Status == OrderStatus.Completed &&
                order.CreatedAt >= startDateUtc &&
                order.CreatedAt <= endDateUtc);

        var shopVisits = await events.CountAsync(item => item.EventType == AnalyticsEventType.ShopVisit, cancellationToken);
        var productViews = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.ProductView &&
                (item.Product == null || item.Product.Type != ProductType.Course),
            cancellationToken);
        var courseViews = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.ProductView &&
                item.Product != null &&
                item.Product.Type == ProductType.Course,
            cancellationToken);
        var mediaViews = await events.CountAsync(item => item.EventType == AnalyticsEventType.MediaView, cancellationToken);
        var uniqueVisitors = await CountUniqueVisitorsAsync(events, cancellationToken);
        var completedSales = await orders.CountAsync(cancellationToken);
        var totalRevenue = await orders.SumAsync(order => order.Amount, cancellationToken);

        var newLikes = await _dbContext.MediaLikes
            .AsNoTracking()
            .CountAsync(
                item =>
                    item.Media.ShopId == shopId &&
                    item.CreatedAt >= startDateUtc &&
                    item.CreatedAt <= endDateUtc,
                cancellationToken);

        var newComments = await _dbContext.MediaComments
            .AsNoTracking()
            .CountAsync(
                item =>
                    item.Media.ShopId == shopId &&
                    item.CreatedAt >= startDateUtc &&
                    item.CreatedAt <= endDateUtc,
                cancellationToken);

        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.ShopId == shopId)
            .Select(product => new
            {
                product.Id,
                product.Title
            })
            .ToListAsync(cancellationToken);

        var viewsByProduct = await events
            .Where(item => item.EventType == AnalyticsEventType.ProductView && item.ProductId.HasValue)
            .GroupBy(item => item.ProductId!.Value)
            .Select(group => new
            {
                ProductId = group.Key,
                Views = group.Count()
            })
            .ToDictionaryAsync(item => item.ProductId, item => item.Views, cancellationToken);

        var salesByProduct = await orders
            .GroupBy(order => order.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Sales = group.Count(),
                Revenue = group.Sum(order => order.Amount)
            })
            .ToDictionaryAsync(item => item.ProductId, item => new { item.Sales, item.Revenue }, cancellationToken);

        var topProducts = products
            .Select(product =>
            {
                viewsByProduct.TryGetValue(product.Id, out var views);
                salesByProduct.TryGetValue(product.Id, out var sales);

                return new WeeklyReportProductDto(
                    product.Id,
                    product.Title,
                    views,
                    sales?.Sales ?? 0,
                    sales?.Revenue ?? 0);
            })
            .OrderByDescending(item => item.Revenue)
            .ThenByDescending(item => item.Sales)
            .ThenByDescending(item => item.Views)
            .Take(3)
            .ToList();

        var eventRows = await events
            .Where(item =>
                item.EventType == AnalyticsEventType.ShopVisit ||
                item.EventType == AnalyticsEventType.ProductView ||
                item.EventType == AnalyticsEventType.MediaView)
            .Select(item => new
            {
                CreatedAt = item.CreatedAt!.Value
            })
            .ToListAsync(cancellationToken);

        var orderRows = await orders
            .Select(order => new
            {
                CreatedAt = order.CreatedAt!.Value,
                order.Amount
            })
            .ToListAsync(cancellationToken);

        var viewsByDate = eventRows
            .GroupBy(item => DateOnly.FromDateTime(item.CreatedAt.Date))
            .ToDictionary(group => group.Key, group => group.Count());
        var salesByDate = orderRows
            .GroupBy(item => DateOnly.FromDateTime(item.CreatedAt.Date))
            .ToDictionary(group => group.Key, group => new
            {
                Sales = group.Count(),
                Revenue = group.Sum(item => item.Amount)
            });

        var dailyPoints = new List<WeeklyReportDailyPointDto>();
        for (var date = DateOnly.FromDateTime(startDateUtc.Date);
             date <= DateOnly.FromDateTime(endDateUtc.Date);
             date = date.AddDays(1))
        {
            viewsByDate.TryGetValue(date, out var views);
            salesByDate.TryGetValue(date, out var sales);

            dailyPoints.Add(new WeeklyReportDailyPointDto(
                date.ToString("yyyy-MM-dd"),
                views,
                sales?.Sales ?? 0,
                sales?.Revenue ?? 0));
        }

        return new WeeklySellerReportData(
            shopId,
            shopName,
            sellerEmail,
            sellerName,
            startDateUtc,
            endDateUtc,
            uniqueVisitors,
            shopVisits,
            productViews,
            courseViews,
            mediaViews,
            newLikes,
            newComments,
            completedSales,
            totalRevenue,
            topProducts,
            dailyPoints);
    }

    private async Task<int> CountUniqueVisitorsAsync(
        IQueryable<Models.Entities.AnalyticsEvent> events,
        CancellationToken cancellationToken)
    {
        var userCount = await events
            .Where(item => item.UserId.HasValue)
            .Select(item => item.UserId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);

        var sessionCount = await events
            .Where(item => !item.UserId.HasValue && item.SessionId != null)
            .Select(item => item.SessionId!)
            .Distinct()
            .CountAsync(cancellationToken);

        var ipCount = await events
            .Where(item => !item.UserId.HasValue && item.SessionId == null && item.IpAddress != null)
            .Select(item => item.IpAddress!)
            .Distinct()
            .CountAsync(cancellationToken);

        return userCount + sessionCount + ipCount;
    }

    private static string BuildEmailBody(WeeklySellerReportData data, string reportUrl)
    {
        return $"""
            <p>Merhaba {System.Net.WebUtility.HtmlEncode(data.SellerName)},</p>
            <p>{System.Net.WebUtility.HtmlEncode(data.ShopName)} icin haftalik magaza raporun hazir.</p>
            <p><a href="{System.Net.WebUtility.HtmlEncode(reportUrl)}">PDF raporunu indir</a></p>
            <ul>
              <li>Tekil ziyaretci: {data.UniqueVisitors}</li>
              <li>Toplam kesif goruntulenmesi: {data.ShopVisits + data.ProductViews + data.CourseViews + data.MediaViews}</li>
              <li>Tamamlanan satis: {data.CompletedSales}</li>
              <li>Toplam gelir: {data.TotalRevenue:0.00}</li>
            </ul>
            """;
    }
}
