using CraftoraApi.Data;
using CraftoraApi.DTOs.Common;
using CraftoraApi.DTOs.Customer;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SellerCustomerService : ISellerCustomerService
{
    private readonly AppDbContext _dbContext;

    public SellerCustomerService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<SellerCustomerListResponseDto> GetCustomersAsync(
        Guid userId,
        string? type,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedType = NormalizeCustomerType(type);

        var snapshots = await BuildCustomerSnapshotsAsync(shop.Id, range, cancellationToken);
        var filteredSnapshots = string.IsNullOrWhiteSpace(normalizedType)
            ? snapshots
            : snapshots.Where(item => item.Type == normalizedType).ToList();

        var totalCount = filteredSnapshots.Count;
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        var items = filteredSnapshots
            .OrderByDescending(item => item.LastActivityAt ?? DateTime.MinValue)
            .ThenByDescending(item => item.TotalSpent)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(MapToListItem)
            .ToList();

        return new SellerCustomerListResponseDto(
            Items: items,
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<SellerCustomerDetailDto> GetCustomerDetailAsync(
        Guid userId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var customer = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == customerId, cancellationToken);

        if (customer is null)
        {
            throw new NotFoundException("Musteri bulunamadi.");
        }

        var hasAnyRelationship = await HasCustomerRelationshipAsync(shop.Id, customerId, cancellationToken);
        if (!hasAnyRelationship)
        {
            throw new NotFoundException("Bu magazaya ait musteri bulunamadi.");
        }

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Product)
            .Where(order => order.ShopId == shop.Id && order.BuyerId == customerId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

        var isSubscriber = await _dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(
                item => item.ShopId == shop.Id && item.UserId == customerId,
                cancellationToken);

        var analyticsActivities = await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item => item.ShopId == shop.Id && item.UserId == customerId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var subscriptionActivities = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(item => item.ShopId == shop.Id && item.UserId == customerId)
            .Select(item => new SellerCustomerActivityDto(
                item.Id,
                "subscription",
                "Magazayi takip etti",
                item.ShopId,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

        var orderActivities = orders
            .Select(order => new SellerCustomerActivityDto(
                order.Id,
                "purchase",
                $"{order.Product.Title} satin alindi",
                order.ProductId,
                order.CreatedAt))
            .ToList();

        var activities = analyticsActivities
            .Select(MapAnalyticsActivity)
            .Concat(subscriptionActivities)
            .Concat(orderActivities)
            .OrderByDescending(item => item.CreatedAt ?? DateTime.MinValue)
            .Take(100)
            .ToList();

        var totalOrders = orders.Count;
        var completedOrders = orders.Where(order => order.Status == OrderStatus.Completed).ToList();
        var totalSpentByCurrency = completedOrders
            .GroupBy(order => CurrencyCode.Normalize(order.Currency))
            .Select(group => new CurrencyAmountDto(
                Currency: group.Key,
                Amount: group.Sum(order => order.Amount)))
            .OrderBy(item => item.Currency)
            .ToList();
        var averageOrderValueByCurrency = completedOrders
            .GroupBy(order => CurrencyCode.Normalize(order.Currency))
            .Select(group => new CurrencyAmountDto(
                Currency: group.Key,
                Amount: Math.Round(
                    group.Sum(order => order.Amount) / group.Count(),
                    2,
                    MidpointRounding.AwayFromZero)))
            .OrderBy(item => item.Currency)
            .ToList();
        var totalSpent = totalSpentByCurrency.Count == 1 ? totalSpentByCurrency[0].Amount : 0;
        var averageOrderValue = averageOrderValueByCurrency.Count == 1
            ? averageOrderValueByCurrency[0].Amount
            : 0;

        return new SellerCustomerDetailDto(
            CustomerId: customer.Id,
            Name: customer.FullName,
            Email: customer.Email,
            AvatarUrl: customer.AvatarUrl,
            JoinedAt: customer.CreatedAt,
            TotalOrders: totalOrders,
            TotalSpent: totalSpent,
            AverageOrderValue: averageOrderValue,
            TotalSpentByCurrency: totalSpentByCurrency,
            AverageOrderValueByCurrency: averageOrderValueByCurrency,
            IsSubscriber: isSubscriber,
            SubscriptionStatus: isSubscriber ? "active" : null,
            LastActivityAt: activities.FirstOrDefault()?.CreatedAt,
            Orders: orders.Select(MapCustomerOrder).ToList(),
            Activities: activities);
    }

    public async Task<SellerCustomerSummaryDto> GetSummaryAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);
        var snapshots = await BuildCustomerSnapshotsAsync(shop.Id, range, cancellationToken);

        var buyers = snapshots.Count(item => item.TotalOrders > 0);
        var subscribers = snapshots.Count(item => item.IsSubscriber);
        var returningCustomers = snapshots.Count(item => item.TotalOrders >= 2);
        var averageCustomerValueByCurrency = snapshots
            .SelectMany(snapshot => snapshot.TotalSpentByCurrency.Select(total => new
            {
                snapshot.CustomerId,
                total.Currency,
                total.Amount
            }))
            .GroupBy(item => item.Currency)
            .Select(group => new CurrencyAmountDto(
                Currency: group.Key,
                Amount: Math.Round(
                    group.Sum(item => item.Amount) / group
                        .Select(item => item.CustomerId)
                        .Distinct()
                        .Count(),
                    2,
                    MidpointRounding.AwayFromZero)))
            .OrderBy(item => item.Currency)
            .ToList();
        var anonymousVisitors = await CountAnonymousVisitorsAsync(shop.Id, range, cancellationToken);
        var namedVisitors = snapshots.Count(item => item.ShopVisitCount > 0 || item.ProductViewCount > 0);

        return new SellerCustomerSummaryDto(
            TotalCustomers: snapshots.Count,
            Buyers: buyers,
            Subscribers: subscribers,
            Visitors: namedVisitors + anonymousVisitors,
            ReturningCustomers: returningCustomers,
            AverageCustomerValue: averageCustomerValueByCurrency.Count == 1
                ? averageCustomerValueByCurrency[0].Amount
                : 0,
            AverageCustomerValueByCurrency: averageCustomerValueByCurrency);
    }

    public async Task<IReadOnlyList<SellerCustomerSegmentDto>> GetSegmentsAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var range = NormalizeRange(startDate, endDate);
        var snapshots = await BuildCustomerSnapshotsAsync(shop.Id, range, cancellationToken);

        var segmentCounts = new Dictionary<string, int>
        {
            ["Buyers"] = snapshots.Count(item => item.Type == "buyer"),
            ["Subscribers"] = snapshots.Count(item => item.Type == "subscriber"),
            ["Leads"] = snapshots.Count(item => item.Type == "lead"),
            ["Visitors"] = snapshots.Count(item => item.Type == "visitor")
        };
        var total = Math.Max(segmentCounts.Values.Sum(), 1);

        return segmentCounts
            .Select(item => new SellerCustomerSegmentDto(
                Label: item.Key,
                Count: item.Value,
                Percentage: Math.Round(item.Value * 100d / total, 2)))
            .ToList();
    }

    private async Task<List<CustomerSnapshot>> BuildCustomerSnapshotsAsync(
        Guid shopId,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var orderStats = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.ShopId == shopId &&
                order.BuyerId != Guid.Empty &&
                order.CreatedAt >= range.Start &&
                order.CreatedAt <= range.End)
            .GroupBy(order => order.BuyerId)
            .Select(group => new
            {
                UserId = group.Key,
                TotalOrders = group.Count(),
                TotalSpent = group
                    .Where(order => order.Status == OrderStatus.Completed)
                    .Sum(order => order.Amount),
                Currency = group
                    .OrderByDescending(order => order.CreatedAt)
                    .Select(order => order.Currency)
                    .FirstOrDefault(),
                CourseCount = group
                    .Where(order => order.Product.Type == ProductType.Course && order.Status == OrderStatus.Completed)
                    .Select(order => order.ProductId)
                    .Distinct()
                    .Count(),
                LastOrderAt = group.Max(order => order.CreatedAt)
            })
            .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var spentRows = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.ShopId == shopId &&
                order.BuyerId != Guid.Empty &&
                order.Status == OrderStatus.Completed &&
                order.CreatedAt >= range.Start &&
                order.CreatedAt <= range.End)
            .GroupBy(order => new { order.BuyerId, order.Currency })
            .Select(group => new
            {
                UserId = group.Key.BuyerId,
                group.Key.Currency,
                Amount = group.Sum(order => order.Amount)
            })
            .ToListAsync(cancellationToken);

        var spentByCustomer = spentRows
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CurrencyAmountDto>)group
                    .GroupBy(item => CurrencyCode.Normalize(item.Currency))
                    .Select(currencyGroup => new CurrencyAmountDto(
                        Currency: currencyGroup.Key,
                        Amount: currencyGroup.Sum(item => item.Amount)))
                    .OrderBy(item => item.Currency)
                    .ToList());

        var subscriptions = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(item =>
                item.ShopId == shopId &&
                item.CreatedAt >= range.Start &&
                item.CreatedAt <= range.End)
            .GroupBy(item => item.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                LastSubscriptionAt = group.Max(item => item.CreatedAt)
            })
            .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var eventStats = await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item =>
                item.ShopId == shopId &&
                item.UserId.HasValue &&
                item.CreatedAt >= range.Start &&
                item.CreatedAt <= range.End)
            .GroupBy(item => item.UserId!.Value)
            .Select(group => new
            {
                UserId = group.Key,
                ProductViewCount = group.Count(item => item.EventType == AnalyticsEventType.ProductView),
                ShopVisitCount = group.Count(item => item.EventType == AnalyticsEventType.ShopVisit),
                LeadEventCount = group.Count(item =>
                    item.EventType == AnalyticsEventType.AddToCart ||
                    item.EventType == AnalyticsEventType.CheckoutStarted),
                LastEventAt = group.Max(item => item.CreatedAt)
            })
            .ToDictionaryAsync(item => item.UserId, cancellationToken);

        var customerIds = orderStats.Keys
            .Concat(subscriptions.Keys)
            .Concat(eventStats.Keys)
            .Distinct()
            .ToList();

        if (customerIds.Count == 0)
        {
            return new List<CustomerSnapshot>();
        }

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(user => customerIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        return customerIds
            .Where(users.ContainsKey)
            .Select(customerId =>
            {
                users.TryGetValue(customerId, out var user);
                orderStats.TryGetValue(customerId, out var order);
                subscriptions.TryGetValue(customerId, out var subscription);
                eventStats.TryGetValue(customerId, out var analytics);
                spentByCustomer.TryGetValue(customerId, out var totalSpentByCurrency);

                var lastActivity = GetLatestActivity(
                    order?.LastOrderAt,
                    subscription?.LastSubscriptionAt,
                    analytics?.LastEventAt);

                return new CustomerSnapshot(
                    CustomerId: customerId,
                    Name: user?.FullName,
                    Email: user?.Email ?? string.Empty,
                    AvatarUrl: user?.AvatarUrl,
                    Type: DetermineCustomerType(
                        order?.TotalOrders ?? 0,
                        subscription is not null,
                        analytics?.LeadEventCount ?? 0,
                        (analytics?.ProductViewCount ?? 0) + (analytics?.ShopVisitCount ?? 0)),
                    TotalOrders: order?.TotalOrders ?? 0,
                    TotalSpent: totalSpentByCurrency?.Count == 1
                        ? totalSpentByCurrency[0].Amount
                        : 0,
                    Currency: order?.Currency,
                    TotalSpentByCurrency: totalSpentByCurrency ?? [],
                    LastActivityAt: lastActivity.At,
                    LastActivityType: lastActivity.Type,
                    IsSubscriber: subscription is not null,
                    CourseCount: order?.CourseCount ?? 0,
                    ProductViewCount: analytics?.ProductViewCount ?? 0,
                    ShopVisitCount: analytics?.ShopVisitCount ?? 0);
            })
            .ToList();
    }

    private async Task<bool> HasCustomerRelationshipAsync(
        Guid shopId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var hasOrder = await _dbContext.Orders
            .AsNoTracking()
            .AnyAsync(order => order.ShopId == shopId && order.BuyerId == customerId, cancellationToken);
        if (hasOrder)
        {
            return true;
        }

        var hasSubscription = await _dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(item => item.ShopId == shopId && item.UserId == customerId, cancellationToken);
        if (hasSubscription)
        {
            return true;
        }

        return await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .AnyAsync(item => item.ShopId == shopId && item.UserId == customerId, cancellationToken);
    }

    private async Task<int> CountAnonymousVisitorsAsync(
        Guid shopId,
        DateRange range,
        CancellationToken cancellationToken)
    {
        var anonymousSessions = await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item =>
                item.ShopId == shopId &&
                !item.UserId.HasValue &&
                item.SessionId != null &&
                item.CreatedAt >= range.Start &&
                item.CreatedAt <= range.End)
            .Select(item => item.SessionId!)
            .Distinct()
            .CountAsync(cancellationToken);

        var anonymousIps = await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item =>
                item.ShopId == shopId &&
                !item.UserId.HasValue &&
                item.SessionId == null &&
                item.IpAddress != null &&
                item.CreatedAt >= range.Start &&
                item.CreatedAt <= range.End)
            .Select(item => item.IpAddress!)
            .Distinct()
            .CountAsync(cancellationToken);

        return anonymousSessions + anonymousIps;
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

    private static SellerCustomerListItemDto MapToListItem(CustomerSnapshot snapshot)
    {
        return new SellerCustomerListItemDto(
            CustomerId: snapshot.CustomerId,
            Name: snapshot.Name,
            Email: snapshot.Email,
            AvatarUrl: snapshot.AvatarUrl,
            Type: snapshot.Type,
            TotalOrders: snapshot.TotalOrders,
            TotalSpent: snapshot.TotalSpent,
            Currency: snapshot.Currency,
            TotalSpentByCurrency: snapshot.TotalSpentByCurrency,
            LastActivityAt: snapshot.LastActivityAt,
            LastActivityType: snapshot.LastActivityType,
            IsSubscriber: snapshot.IsSubscriber,
            CourseCount: snapshot.CourseCount,
            ProductViewCount: snapshot.ProductViewCount,
            ShopVisitCount: snapshot.ShopVisitCount);
    }

    private static SellerCustomerOrderDto MapCustomerOrder(Order order)
    {
        return new SellerCustomerOrderDto(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            ProductTitle: order.Product.Title,
            Amount: order.Amount,
            Currency: CurrencyCode.Normalize(order.Currency),
            Status: ToOrderStatusName(order.Status),
            CreatedAt: order.CreatedAt);
    }

    private static SellerCustomerActivityDto MapAnalyticsActivity(AnalyticsEvent analyticsEvent)
    {
        var type = ToActivityType(analyticsEvent);
        var title = analyticsEvent.Product is null
            ? ToActivityTitle(type)
            : $"{analyticsEvent.Product.Title}: {ToActivityTitle(type)}";

        return new SellerCustomerActivityDto(
            Id: analyticsEvent.Id,
            Type: type,
            Title: title,
            TargetId: analyticsEvent.ProductId ?? analyticsEvent.OrderId ?? (Guid?)analyticsEvent.ShopId,
            CreatedAt: analyticsEvent.CreatedAt);
    }

    private static (DateTime? At, string? Type) GetLatestActivity(
        DateTime? orderAt,
        DateTime? subscriptionAt,
        DateTime? eventAt)
    {
        var activities = new[]
        {
            (At: orderAt, Type: "purchase"),
            (At: subscriptionAt, Type: "subscription"),
            (At: eventAt, Type: "product_view")
        };

        return activities
            .Where(item => item.At.HasValue)
            .OrderByDescending(item => item.At)
            .FirstOrDefault();
    }

    private static string DetermineCustomerType(
        int totalOrders,
        bool isSubscriber,
        int leadEventCount,
        int visitEventCount)
    {
        if (totalOrders > 0)
        {
            return "buyer";
        }

        if (isSubscriber)
        {
            return "subscriber";
        }

        if (leadEventCount > 0)
        {
            return "lead";
        }

        return visitEventCount > 0 ? "visitor" : "visitor";
    }

    private static string? NormalizeCustomerType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "buyer" => "buyer",
            "subscriber" => "subscriber",
            "visitor" => "visitor",
            "lead" => "lead",
            _ => throw new BadRequestException("Gecersiz musteri type degeri.")
        };
    }

    private static DateRange NormalizeRange(DateTime? startDate, DateTime? endDate)
    {
        var end = (endDate ?? DateTime.UtcNow).ToUniversalTime();
        var start = (startDate ?? end.AddDays(-30)).ToUniversalTime();

        if (start > end)
        {
            throw new BadRequestException("Baslangic tarihi bitis tarihinden buyuk olamaz.");
        }

        return new DateRange(start, end);
    }

    private static string ToActivityType(AnalyticsEvent analyticsEvent)
    {
        return analyticsEvent.EventType switch
        {
            AnalyticsEventType.ShopVisit => "shop_visit",
            AnalyticsEventType.ProductView when analyticsEvent.Product?.Type == ProductType.Course => "course_view",
            AnalyticsEventType.ProductView => "product_view",
            AnalyticsEventType.AddToCart => "add_to_cart",
            AnalyticsEventType.CheckoutStarted => "checkout_started",
            AnalyticsEventType.PurchaseCompleted => "purchase",
            AnalyticsEventType.DownloadClicked => "download_clicked",
            _ => analyticsEvent.EventType.ToString()
        };
    }

    private static string ToActivityTitle(string type)
    {
        return type switch
        {
            "shop_visit" => "Magaza ziyareti",
            "course_view" => "Kurs goruntulendi",
            "product_view" => "Urun goruntulendi",
            "add_to_cart" => "Sepete eklendi",
            "checkout_started" => "Odeme baslatildi",
            "purchase" => "Satin alma tamamlandi",
            "download_clicked" => "Dosya indirme tiklandi",
            _ => "Aktivite"
        };
    }

    private static string ToOrderStatusName(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Pending => "pending",
            OrderStatus.Completed => "completed",
            OrderStatus.Failed => "failed",
            OrderStatus.Refunded => "refunded",
            _ => status.ToString()
        };
    }

    private sealed record DateRange(DateTime Start, DateTime End);

    private sealed record CustomerSnapshot(
        Guid CustomerId,
        string? Name,
        string Email,
        string? AvatarUrl,
        string Type,
        int TotalOrders,
        decimal TotalSpent,
        string? Currency,
        IReadOnlyList<CurrencyAmountDto> TotalSpentByCurrency,
        DateTime? LastActivityAt,
        string? LastActivityType,
        bool IsSubscriber,
        int CourseCount,
        int ProductViewCount,
        int ShopVisitCount);
}
