using CraftoraApi.Data;
using CraftoraApi.DTOs.Analytics;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SellerAnalyticsService : ISellerAnalyticsService
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);

    private readonly AppDbContext _dbContext;

    public SellerAnalyticsService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<SellerAnalyticsOverviewDto> GetOverviewAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);

        var events = AnalyticsEventsForShop(shop.Id, range);
        var orders = CompletedOrdersForShop(shop.Id, range);

        var shopVisits = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.ShopVisit,
            cancellationToken);
        var productViews = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.ProductView &&
                (item.Product == null || item.Product.Type != ProductType.Course),
            cancellationToken);
        var courseViews = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.ProductView &&
                item.Product != null &&
                item.Product.Type == ProductType.Course,
            cancellationToken);
        var mediaViews = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.MediaView,
            cancellationToken);
        var totalDiscoveryViews = shopVisits + productViews + courseViews + mediaViews;
        var addToCartCount = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.AddToCart,
            cancellationToken);
        var checkoutStartedCount = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.CheckoutStarted,
            cancellationToken);
        var purchaseCompletedCount = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.PurchaseCompleted,
            cancellationToken);
        var totalRevenue = await orders.SumAsync(order => order.Amount, cancellationToken);
        var uniqueCustomers = await orders
            .Select(order => order.BuyerId)
            .Distinct()
            .CountAsync(cancellationToken);
        var uniqueVisitors = await CountUniqueVisitorsAsync(events, cancellationToken);
        var averageCourseCompletionRate = await CalculateAverageCourseCompletionRateAsync(
            shop.Id,
            range,
            cancellationToken);

        return new SellerAnalyticsOverviewDto(
            ShopId: shop.Id,
            StartDate: range.Start,
            EndDate: range.End,
            TotalProductViews: productViews,
            TotalCourseViews: courseViews,
            TotalShopVisits: shopVisits,
            TotalMediaViews: mediaViews,
            TotalDiscoveryViews: totalDiscoveryViews,
            AddToCartCount: addToCartCount,
            CheckoutStartedCount: checkoutStartedCount,
            PurchaseCompletedCount: purchaseCompletedCount,
            TotalRevenue: totalRevenue,
            UniqueVisitors: uniqueVisitors,
            UniqueCustomers: uniqueCustomers,
            PurchaseConversionRate: CalculateRate(purchaseCompletedCount, productViews),
            AverageCourseCompletionRate: averageCourseCompletionRate);
    }

    public async Task<SellerAnalyticsFunnelDto> GetFunnelAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);
        var events = AnalyticsEventsForShop(shop.Id, range);

        var views = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.ProductView,
            cancellationToken);
        var addToCart = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.AddToCart,
            cancellationToken);
        var checkout = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.CheckoutStarted,
            cancellationToken);
        var purchase = await events.CountAsync(
            item => item.EventType == AnalyticsEventType.PurchaseCompleted,
            cancellationToken);

        var steps = new List<FunnelStepDto>
        {
            new("product_view", "Goruntuleme", views, 0),
            new("add_to_cart", "Sepete ekleme", addToCart, CalculateDropOff(views, addToCart)),
            new("checkout_started", "Odeme baslatma", checkout, CalculateDropOff(addToCart, checkout)),
            new("purchase_completed", "Satin alma", purchase, CalculateDropOff(checkout, purchase))
        };

        return new SellerAnalyticsFunnelDto(range.Start, range.End, steps);
    }

    public async Task<IReadOnlyList<TrafficSourceDto>> GetTrafficSourcesAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);

        var groupedSources = await AnalyticsEventsForShop(shop.Id, range)
            .Where(item =>
                item.EventType == AnalyticsEventType.ProductView ||
                item.EventType == AnalyticsEventType.ShopVisit ||
                item.EventType == AnalyticsEventType.MediaView)
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Source) ? "direct" : item.Source!)
            .Select(group => new
            {
                Source = group.Key,
                Visits = group.Count()
            })
            .OrderByDescending(item => item.Visits)
            .ToListAsync(cancellationToken);

        var totalVisits = groupedSources.Sum(item => item.Visits);

        return groupedSources
            .Select(item => new TrafficSourceDto(
                Source: item.Source,
                Visits: item.Visits,
                Percentage: CalculateRate(item.Visits, totalVisits)))
            .ToList();
    }

    public async Task<IReadOnlyList<TopProductAnalyticsDto>> GetTopProductsAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);
        var safeLimit = Math.Clamp(limit, 1, 50);

        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.ShopId == shop.Id && product.IsActive == true)
            .Select(product => new
            {
                product.Id,
                product.Title,
                product.Type
            })
            .ToListAsync(cancellationToken);

        var viewsByProduct = await AnalyticsEventsForShop(shop.Id, range)
            .Where(item => item.EventType == AnalyticsEventType.ProductView && item.ProductId.HasValue)
            .GroupBy(item => item.ProductId!.Value)
            .Select(group => new
            {
                ProductId = group.Key,
                Views = group.Count()
            })
            .ToDictionaryAsync(item => item.ProductId, item => item.Views, cancellationToken);

        var salesByProduct = await CompletedOrdersForShop(shop.Id, range)
            .GroupBy(order => order.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Sales = group.Count(),
                Revenue = group.Sum(order => order.Amount)
            })
            .ToDictionaryAsync(item => item.ProductId, item => new { item.Sales, item.Revenue }, cancellationToken);

        return products
            .Select(product =>
            {
                viewsByProduct.TryGetValue(product.Id, out var views);
                salesByProduct.TryGetValue(product.Id, out var sales);

                return new TopProductAnalyticsDto(
                    ProductId: product.Id,
                    Title: product.Title,
                    ProductType: ToProductTypeName(product.Type),
                    Views: views,
                    Sales: sales?.Sales ?? 0,
                    Revenue: sales?.Revenue ?? 0,
                    ViewToPurchaseRate: CalculateRate(sales?.Sales ?? 0, views));
            })
            .OrderByDescending(item => item.Revenue)
            .ThenByDescending(item => item.Sales)
            .ThenByDescending(item => item.Views)
            .Take(safeLimit)
            .ToList();
    }

    public async Task<SellerAnalyticsTimeseriesDto> GetTimeseriesAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        string? granularity,
        CancellationToken cancellationToken = default)
    {
        var normalizedGranularity = string.IsNullOrWhiteSpace(granularity)
            ? "day"
            : granularity.Trim().ToLowerInvariant();
        if (normalizedGranularity != "day")
        {
            throw new BadRequestException("Sadece day granularity destekleniyor.");
        }

        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeTimeseriesRange(startDate, endDate);

        var eventRows = await AnalyticsEventsForShop(shop.Id, range)
            .Where(item =>
                item.EventType == AnalyticsEventType.ProductView ||
                item.EventType == AnalyticsEventType.ShopVisit ||
                item.EventType == AnalyticsEventType.MediaView ||
                item.EventType == AnalyticsEventType.AddToCart ||
                item.EventType == AnalyticsEventType.CheckoutStarted ||
                item.EventType == AnalyticsEventType.PurchaseCompleted)
            .Select(item => new
                TimeseriesEventRow(
                item.CreatedAt!.Value,
                item.EventType,
                item.Product == null
                    ? (ProductType?)null
                    : item.Product.Type,
                item.UserId,
                item.SessionId,
                item.IpAddress == null ? null : item.IpAddress.ToString()))
            .ToListAsync(cancellationToken);

        var orderRows = await CompletedOrdersForShop(shop.Id, range)
            .Select(order => new
            {
                CreatedAt = order.CreatedAt!.Value,
                order.Amount
            })
            .ToListAsync(cancellationToken);

        var eventsByDate = eventRows
            .GroupBy(item => DateOnly.FromDateTime(item.CreatedAt.Date))
            .ToDictionary(group => group.Key, group => group.ToList());
        var revenueByDate = orderRows
            .GroupBy(item => DateOnly.FromDateTime(item.CreatedAt.Date))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        var points = new List<SellerAnalyticsTimeseriesPointDto>();
        for (var date = DateOnly.FromDateTime(range.Start.Date);
             date <= DateOnly.FromDateTime(range.End.Date);
             date = date.AddDays(1))
        {
            eventsByDate.TryGetValue(date, out var dayEvents);
            dayEvents ??= [];
            revenueByDate.TryGetValue(date, out var revenue);

            var courseViews = dayEvents.Count(item =>
                item.EventType == AnalyticsEventType.ProductView &&
                item.ProductType == ProductType.Course);
            var productViews = dayEvents.Count(item =>
                item.EventType == AnalyticsEventType.ProductView &&
                item.ProductType != ProductType.Course);
            var shopVisits = dayEvents.Count(item => item.EventType == AnalyticsEventType.ShopVisit);
            var mediaViews = dayEvents.Count(item => item.EventType == AnalyticsEventType.MediaView);

            points.Add(new SellerAnalyticsTimeseriesPointDto(
                Date: date.ToString("yyyy-MM-dd"),
                ShopVisits: shopVisits,
                ProductViews: productViews,
                CourseViews: courseViews,
                MediaViews: mediaViews,
                TotalViews: shopVisits + productViews + courseViews + mediaViews,
                AddToCartCount: dayEvents.Count(item => item.EventType == AnalyticsEventType.AddToCart),
                CheckoutStartedCount: dayEvents.Count(item => item.EventType == AnalyticsEventType.CheckoutStarted),
                PurchaseCompletedCount: dayEvents.Count(item => item.EventType == AnalyticsEventType.PurchaseCompleted),
                Revenue: revenue,
                UniqueVisitors: CountUniqueVisitors(dayEvents)));
        }

        return new SellerAnalyticsTimeseriesDto(
            StartDate: range.Start,
            EndDate: range.End,
            Granularity: normalizedGranularity,
            Points: points);
    }

    public async Task<IReadOnlyList<CourseAnalyticsDto>> GetCoursesAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);

        var courses = await GetCourseBasicsAsync(shop.Id, cancellationToken);
        var courseStats = await BuildCourseStatsAsync(shop.Id, range, cancellationToken);

        return courses
            .Select(course =>
            {
                var stats = courseStats.GetValueOrDefault(course.CourseId);
                return new CourseAnalyticsDto(
                    CourseId: course.CourseId,
                    ProductId: course.ProductId,
                    Title: course.Title,
                    Level: course.Level,
                    Views: stats?.Views ?? 0,
                    Sales: stats?.Sales ?? 0,
                    Revenue: stats?.Revenue ?? 0,
                    TotalLessons: course.TotalLessons,
                    StartedStudents: stats?.StartedStudents ?? 0,
                    CompletedStudents: stats?.CompletedStudents ?? 0,
                    AverageCompletionRate: stats?.AverageCompletionRate ?? 0);
            })
            .OrderByDescending(course => course.Revenue)
            .ThenByDescending(course => course.Sales)
            .ThenByDescending(course => course.Views)
            .ToList();
    }

    public async Task<CourseAnalyticsDetailDto> GetCourseDetailAsync(
        Guid userId,
        Guid courseId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);

        var course = await _dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Product)
            .Include(item => item.CourseSections)
                .ThenInclude(section => section.CourseLessons)
            .FirstOrDefaultAsync(
                item => item.Id == courseId &&
                    item.Product.ShopId == shop.Id &&
                    item.Product.IsActive == true,
                cancellationToken);

        if (course is null)
        {
            throw new NotFoundException("Kurs bulunamadi.");
        }

        var courseStats = await BuildCourseStatsAsync(shop.Id, range, cancellationToken);
        var stats = courseStats.GetValueOrDefault(course.Id);

        var lessons = course.CourseSections
            .Where(section => section.IsActive)
            .SelectMany(section => section.CourseLessons.Where(lesson => lesson.IsActive))
            .OrderBy(lesson => lesson.CourseSection.SortOrder)
            .ThenBy(lesson => lesson.SortOrder)
            .ToList();

        var lessonIds = lessons.Select(lesson => lesson.Id).ToList();
        var lessonProgress = await _dbContext.UserLessonProgresses
            .AsNoTracking()
            .Where(progress => lessonIds.Contains(progress.CourseLessonId))
            .GroupBy(progress => progress.CourseLessonId)
            .Select(group => new
            {
                LessonId = group.Key,
                StartedStudents = group.Select(progress => progress.UserId).Distinct().Count(),
                CompletedStudents = group
                    .Where(progress => progress.IsCompleted)
                    .Select(progress => progress.UserId)
                    .Distinct()
                    .Count()
            })
            .ToDictionaryAsync(item => item.LessonId, cancellationToken);

        var lessonDtos = lessons
            .Select(lesson =>
            {
                lessonProgress.TryGetValue(lesson.Id, out var progress);
                var startedStudents = progress?.StartedStudents ?? 0;
                var completedStudents = progress?.CompletedStudents ?? 0;

                return new CourseLessonAnalyticsDto(
                    LessonId: lesson.Id,
                    Title: lesson.Title,
                    SortOrder: lesson.SortOrder,
                    StartedStudents: startedStudents,
                    CompletedStudents: completedStudents,
                    CompletionRate: CalculateRate(completedStudents, startedStudents));
            })
            .ToList();

        return new CourseAnalyticsDetailDto(
            CourseId: course.Id,
            ProductId: course.ProductId,
            Title: course.Product.Title,
            Level: course.Level,
            Views: stats?.Views ?? 0,
            Sales: stats?.Sales ?? 0,
            Revenue: stats?.Revenue ?? 0,
            TotalLessons: lessons.Count,
            StartedStudents: stats?.StartedStudents ?? 0,
            CompletedStudents: stats?.CompletedStudents ?? 0,
            AverageCompletionRate: stats?.AverageCompletionRate ?? 0,
            Lessons: lessonDtos);
    }

    private IQueryable<AnalyticsEvent> AnalyticsEventsForShop(Guid shopId, DateRange range)
    {
        return _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item =>
                item.ShopId == shopId &&
                item.CreatedAt >= range.Start &&
                item.CreatedAt <= range.End);
    }

    private IQueryable<Order> CompletedOrdersForShop(Guid shopId, DateRange range)
    {
        return _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.ShopId == shopId &&
                order.Status == OrderStatus.Completed &&
                order.CreatedAt >= range.Start &&
                order.CreatedAt <= range.End);
    }

    private async Task<Shop> GetSellerShopAsync(Guid userId, CancellationToken cancellationToken)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.IsActive == true,
                cancellationToken);

        if (shop is null)
        {
            throw new NotFoundException("Aktif magaza bulunamadi.");
        }

        return shop;
    }

    private async Task<int> CountUniqueVisitorsAsync(
        IQueryable<AnalyticsEvent> events,
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

    private async Task<IReadOnlyList<CourseBasic>> GetCourseBasicsAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Courses
            .AsNoTracking()
            .Where(course =>
                course.Product.ShopId == shopId &&
                course.Product.IsActive == true)
            .Select(course => new CourseBasic(
                course.Id,
                course.ProductId,
                course.Product.Title,
                course.Level,
                course.CourseSections
                    .Where(section => section.IsActive)
                    .SelectMany(section => section.CourseLessons)
                    .Count(lesson => lesson.IsActive)))
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, CourseStats>> BuildCourseStatsAsync(
        Guid shopId,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var courses = await GetCourseBasicsAsync(shopId, cancellationToken);
        var productIdToCourse = courses.ToDictionary(course => course.ProductId, course => course);

        var courseProductIds = productIdToCourse.Keys.ToList();
        if (courseProductIds.Count == 0)
        {
            return new Dictionary<Guid, CourseStats>();
        }

        var viewsByProduct = await AnalyticsEventsForShop(shopId, range)
            .Where(item =>
                item.EventType == AnalyticsEventType.ProductView &&
                item.ProductId.HasValue &&
                courseProductIds.Contains(item.ProductId.Value))
            .GroupBy(item => item.ProductId!.Value)
            .Select(group => new
            {
                ProductId = group.Key,
                Views = group.Count()
            })
            .ToDictionaryAsync(item => item.ProductId, item => item.Views, cancellationToken);

        var salesByProduct = await CompletedOrdersForShop(shopId, range)
            .Where(order => courseProductIds.Contains(order.ProductId))
            .GroupBy(order => order.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Sales = group.Count(),
                Revenue = group.Sum(order => order.Amount)
            })
            .ToDictionaryAsync(item => item.ProductId, item => new { item.Sales, item.Revenue }, cancellationToken);

        var courseIds = courses.Select(course => course.CourseId).ToList();
        var progressRows = await _dbContext.UserLessonProgresses
            .AsNoTracking()
            .Where(progress =>
                courseIds.Contains(progress.CourseLesson.CourseSection.CourseId))
            .GroupBy(progress => new
            {
                progress.CourseLesson.CourseSection.CourseId,
                progress.UserId
            })
            .Select(group => new
            {
                group.Key.CourseId,
                group.Key.UserId,
                CompletedLessons = group.Count(progress => progress.IsCompleted)
            })
            .ToListAsync(cancellationToken);

        return courses.ToDictionary(
            course => course.CourseId,
            course =>
            {
                viewsByProduct.TryGetValue(course.ProductId, out var views);
                salesByProduct.TryGetValue(course.ProductId, out var sales);

                var courseProgressRows = progressRows
                    .Where(progress => progress.CourseId == course.CourseId)
                    .ToList();
                var startedStudents = courseProgressRows
                    .Select(progress => progress.UserId)
                    .Distinct()
                    .Count();
                var completedStudents = course.TotalLessons == 0
                    ? 0
                    : courseProgressRows.Count(progress => progress.CompletedLessons >= course.TotalLessons);
                var averageCompletionRate = course.TotalLessons == 0 || courseProgressRows.Count == 0
                    ? 0
                    : Math.Round(courseProgressRows.Average(progress =>
                        Math.Min(100d, progress.CompletedLessons * 100d / course.TotalLessons)), 2);

                return new CourseStats(
                    Views: views,
                    Sales: sales?.Sales ?? 0,
                    Revenue: sales?.Revenue ?? 0,
                    StartedStudents: startedStudents,
                    CompletedStudents: completedStudents,
                    AverageCompletionRate: averageCompletionRate);
            });
    }

    private async Task<double> CalculateAverageCourseCompletionRateAsync(
        Guid shopId,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var courseStats = await BuildCourseStatsAsync(
            shopId,
            range,
            cancellationToken);

        var coursesWithStudents = courseStats.Values
            .Where(stats => stats.StartedStudents > 0)
            .ToList();

        return coursesWithStudents.Count == 0
            ? 0
            : Math.Round(coursesWithStudents.Average(stats => stats.AverageCompletionRate), 2);
    }

    private static DateRange NormalizeRange(DateTime? startDate, DateTime? endDate)
    {
        var end = (endDate ?? DateTime.UtcNow).ToUniversalTime();
        var start = (startDate ?? end.Subtract(DefaultRange)).ToUniversalTime();

        if (endDate.HasValue && end.TimeOfDay == TimeSpan.Zero)
        {
            end = end.Date.AddDays(1).AddTicks(-1);
        }

        if (start > end)
        {
            throw new BadRequestException("Baslangic tarihi bitis tarihinden buyuk olamaz.");
        }

        return new DateRange(start, end);
    }

    private static DateRange NormalizeTimeseriesRange(DateTime? startDate, DateTime? endDate)
    {
        var normalized = NormalizeRange(startDate, endDate);
        var start = normalized.Start.Date;
        var end = normalized.End.Date.AddDays(1).AddTicks(-1);

        return new DateRange(start, end);
    }

    private static int CountUniqueVisitors(IReadOnlyCollection<TimeseriesEventRow> events)
    {
        var userCount = events
            .Where(item => item.UserId.HasValue)
            .Select(item => item.UserId!.Value)
            .Distinct()
            .Count();

        var sessionCount = events
            .Where(item => !item.UserId.HasValue && !string.IsNullOrWhiteSpace(item.SessionId))
            .Select(item => item.SessionId!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var ipCount = events
            .Where(item =>
                !item.UserId.HasValue &&
                string.IsNullOrWhiteSpace(item.SessionId) &&
                !string.IsNullOrWhiteSpace(item.IpAddress))
            .Select(item => item.IpAddress!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return userCount + sessionCount + ipCount;
    }

    private static double CalculateRate(int numerator, int denominator)
    {
        return denominator <= 0
            ? 0
            : Math.Round(numerator * 100d / denominator, 2);
    }

    private static double CalculateDropOff(int previous, int current)
    {
        if (previous <= 0)
        {
            return 0;
        }

        var retainedRate = current * 100d / previous;
        return Math.Round(Math.Max(0, 100d - retainedRate), 2);
    }

    private static string ToProductTypeName(ProductType productType)
    {
        return productType == ProductType.Course ? "course" : "digital_file";
    }

    private sealed record DateRange(DateTime Start, DateTime End);

    private sealed record TimeseriesEventRow(
        DateTime CreatedAt,
        AnalyticsEventType EventType,
        ProductType? ProductType,
        Guid? UserId,
        string? SessionId,
        string? IpAddress);

    private sealed record CourseBasic(
        Guid CourseId,
        Guid ProductId,
        string Title,
        string Level,
        int TotalLessons);

    private sealed record CourseStats(
        int Views,
        int Sales,
        decimal Revenue,
        int StartedStudents,
        int CompletedStudents,
        double AverageCompletionRate);
}
