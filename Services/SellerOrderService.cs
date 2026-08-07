using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.DTOs.Order;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SellerOrderService : ISellerOrderService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicMediaUrlExpiryMinutes = 60;

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly IPaymentService _paymentService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SellerOrderService> _logger;

    public SellerOrderService(
        AppDbContext dbContext,
        IStorageService storageService,
        IPaymentService paymentService,
        INotificationService notificationService,
        ILogger<SellerOrderService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SellerOrderListResponseDto> GetSellerOrdersAsync(
        Guid userId,
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);

        var query = BuildSellerOrdersQuery(shop.Id, status, startDate, endDate);
        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new SellerOrderListResponseDto(
            Items: orders.Select(MapToListItem).ToList(),
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<SellerOrderDetailDto> GetSellerOrderDetailAsync(
        Guid userId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);

        var order = await SellerOrdersBaseQuery(shop.Id)
            .FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new NotFoundException("Siparis bulunamadi.");
        }

        var hasLibraryAccess = await _dbContext.UserLibraries
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == order.BuyerId && item.ProductId == order.ProductId,
                cancellationToken);

        return MapToDetail(order, hasLibraryAccess);
    }

    public async Task<SellerOrderSummaryDto> GetSellerOrderSummaryAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var query = ApplyDateFilters(
            _dbContext.Orders.AsNoTracking().Where(order => order.ShopId == shop.Id),
            startDate,
            endDate);

        var totalOrders = await query.CountAsync(cancellationToken);
        var paidOrders = await query.CountAsync(
            order => order.Status == OrderStatus.Completed,
            cancellationToken);
        var failedOrders = await query.CountAsync(
            order => order.Status == OrderStatus.Failed,
            cancellationToken);
        var refundedOrders = await query.CountAsync(
            order => order.Status == OrderStatus.Refunded,
            cancellationToken);
        var financialRows = await query
            .Where(order =>
                order.Status == OrderStatus.Completed ||
                order.Status == OrderStatus.Pending)
            .Select(order => new
            {
                order.Status,
                order.Amount,
                order.Currency
            })
            .ToListAsync(cancellationToken);

        var totalsByCurrency = financialRows
            .GroupBy(order => CurrencyCode.Normalize(order.Currency))
            .Select(group =>
            {
                var paidCurrencyOrders = group
                    .Where(order => order.Status == OrderStatus.Completed)
                    .ToList();
                var currencyRevenue = paidCurrencyOrders.Sum(order => order.Amount);

                return new SellerOrderCurrencySummaryDto(
                    Currency: group.Key,
                    PaidOrders: paidCurrencyOrders.Count,
                    TotalRevenue: currencyRevenue,
                    PendingAmount: group
                        .Where(order => order.Status == OrderStatus.Pending)
                        .Sum(order => order.Amount),
                    AverageOrderValue: paidCurrencyOrders.Count == 0
                        ? 0
                        : Math.Round(
                            currencyRevenue / paidCurrencyOrders.Count,
                            2,
                            MidpointRounding.AwayFromZero));
            })
            .OrderBy(item => item.Currency)
            .ToList();

        var singleCurrencyTotal = totalsByCurrency.Count == 1 ? totalsByCurrency[0] : null;
        var totalRevenue = singleCurrencyTotal?.TotalRevenue ?? 0;
        var pendingAmount = singleCurrencyTotal?.PendingAmount ?? 0;
        var averageOrderValue = singleCurrencyTotal?.AverageOrderValue ?? 0;

        return new SellerOrderSummaryDto(
            TotalOrders: totalOrders,
            PaidOrders: paidOrders,
            FailedOrders: failedOrders,
            RefundedOrders: refundedOrders,
            TotalRevenue: totalRevenue,
            PendingAmount: pendingAmount,
            AverageOrderValue: averageOrderValue,
            TotalsByCurrency: totalsByCurrency);
    }

    public async Task<RefundOrderResponseDto> RefundOrderAsync(
        Guid userId,
        Guid orderId,
        RefundOrderRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = PlainTextInputValidator.Require(request.Reason, "Iade nedeni", 500);

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var order = await _dbContext.Orders
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.lock_seller_refundable_order({orderId})
                """)
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            throw new BadRequestException(
                "Siparis bulunamadi, size ait degil veya iade edilebilir durumda degil.");
        }

        var payment = await _dbContext.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.OrderId == order.Id,
                cancellationToken);

        if (payment is null ||
            payment.Status != PaymentStatusType.Succeeded ||
            string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
        {
            throw new ConflictException("Siparisin iade edilebilir basarili odemesi bulunamadi.");
        }

        if (!string.Equals(payment.PaymentProvider, "mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "Bu odeme saglayicisi icin iade entegrasyonu henuz yapilandirilmadi.");
        }

        var currency = string.IsNullOrWhiteSpace(order.Currency) ? "USD" : order.Currency;
        var refundResult = await _paymentService.RefundPaymentAsync(
            payment.ProviderTransactionId,
            order.Amount,
            currency);

        if (!refundResult.IsSuccess)
        {
            throw new BadRequestException(
                string.IsNullOrWhiteSpace(refundResult.ErrorMessage)
                    ? "Odeme iadesi tamamlanamadi."
                    : refundResult.ErrorMessage);
        }

        var finalized = await _dbContext.Database
            .SqlQuery<bool>($"""
                SELECT public.complete_seller_order_refund(
                    {order.Id},
                    {reason},
                    {refundResult.RefundId}) AS "Value"
                """)
            .SingleAsync(cancellationToken);

        if (!finalized)
        {
            throw new ConflictException("Siparis iadesi veritabaninda tamamlanamadi.");
        }

        await transaction.CommitAsync(cancellationToken);

        try
        {
            await _notificationService.SendNotificationAsync(
                order.BuyerId,
                "Siparisiniz iade edildi",
                $"{order.OrderNumber} numarali siparisinizin {order.Amount:0.00} {currency} tutarindaki odemesi iade edildi.",
                NotificationType.System,
                order.Id);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Refund completed but buyer notification failed. OrderId: {OrderId}",
                order.Id);
        }

        return new RefundOrderResponseDto(
            OrderId: order.Id,
            Status: "refunded",
            RefundedAmount: order.Amount,
            ProviderRefundId: refundResult.RefundId,
            RefundedAt: DateTime.UtcNow);
    }

    private IQueryable<Order> BuildSellerOrdersQuery(
        Guid shopId,
        string? status,
        DateTime? startDate,
        DateTime? endDate)
    {
        var query = SellerOrdersBaseQuery(shopId);
        query = ApplyDateFilters(query, startDate, endDate);

        var parsedStatus = ParseOrderStatus(status);
        if (parsedStatus.HasValue)
        {
            query = query.Where(order => order.Status == parsedStatus.Value);
        }

        return query;
    }

    private IQueryable<Order> SellerOrdersBaseQuery(Guid shopId)
    {
        return _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Product)
            .Include(order => order.Buyer)
            .Include(order => order.Payment)
            .Where(order => order.ShopId == shopId);
    }

    private static IQueryable<Order> ApplyDateFilters(
        IQueryable<Order> query,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (startDate.HasValue)
        {
            var start = startDate.Value.ToUniversalTime();
            query = query.Where(order => order.CreatedAt >= start);
        }

        if (endDate.HasValue)
        {
            var end = endDate.Value.ToUniversalTime();
            query = query.Where(order => order.CreatedAt <= end);
        }

        return query;
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

    private SellerOrderListItemDto MapToListItem(Order order)
    {
        return new SellerOrderListItemDto(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            ProductId: order.ProductId,
            ProductTitle: order.Product.Title,
            ProductCoverImagePublicUrl: GeneratePublicAssetUrl(order.Product.CoverImageUrl),
            ProductType: ToProductTypeName(order.Product.Type),
            BuyerId: order.BuyerId,
            BuyerName: order.Buyer.FullName,
            BuyerEmail: order.Buyer.Email,
            Amount: order.Amount,
            Currency: order.Currency,
            PlatformFee: order.PlatformFee,
            SellerEarnings: order.SellerEarnings,
            PaymentStatus: order.Payment is null ? null : ToPaymentStatusName(order.Payment.Status),
            OrderStatus: ToOrderStatusName(order.Status),
            CreatedAt: order.CreatedAt,
            PaidAt: order.Payment?.Status is PaymentStatusType.Succeeded or PaymentStatusType.Refunded
                ? order.Payment.CreatedAt
                : null,
            HasProductFile: !string.IsNullOrWhiteSpace(order.Product.FileUrl),
            ProductFileName: GetFileName(order.Product.FileUrl),
            InvoicePdfUrl: order.InvoicePdfUrl,
            RefundedAt: order.RefundedAt);
    }

    private SellerOrderDetailDto MapToDetail(Order order, bool hasLibraryAccess)
    {
        return new SellerOrderDetailDto(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            ProductId: order.ProductId,
            ProductTitle: order.Product.Title,
            ProductCoverImagePublicUrl: GeneratePublicAssetUrl(order.Product.CoverImageUrl),
            ProductType: ToProductTypeName(order.Product.Type),
            BuyerId: order.BuyerId,
            BuyerName: order.Buyer.FullName,
            BuyerEmail: order.Buyer.Email,
            Amount: order.Amount,
            Currency: order.Currency,
            PlatformFee: order.PlatformFee,
            SellerEarnings: order.SellerEarnings,
            PaymentStatus: order.Payment is null ? null : ToPaymentStatusName(order.Payment.Status),
            OrderStatus: ToOrderStatusName(order.Status),
            CreatedAt: order.CreatedAt,
            PaidAt: order.Payment?.Status is PaymentStatusType.Succeeded or PaymentStatusType.Refunded
                ? order.Payment.CreatedAt
                : null,
            HasProductFile: !string.IsNullOrWhiteSpace(order.Product.FileUrl),
            ProductFileName: GetFileName(order.Product.FileUrl),
            InvoicePdfUrl: order.InvoicePdfUrl,
            PaymentProvider: order.Payment?.PaymentProvider,
            ProviderTransactionId: order.Payment?.ProviderTransactionId,
            PaymentErrorMessage: order.Payment?.ErrorMessage,
            RefundedAt: order.RefundedAt,
            RefundReason: order.RefundReason,
            AccessStatus: order.Status == OrderStatus.Refunded
                ? "revoked"
                : hasLibraryAccess ? "delivered" : "pending",
            CourseEnrollmentStatus: order.Product.Type == ProductType.Course
                ? order.Status == OrderStatus.Refunded
                    ? "revoked"
                    : hasLibraryAccess ? "enrolled" : "not_enrolled"
                : null);
    }

    private string? GeneratePublicAssetUrl(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PublicAssetsBucketName,
            objectKey,
            PublicMediaUrlExpiryMinutes);
    }

    private static string? GetFileName(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        var normalizedObjectKey = objectKey.Trim();
        var separatorIndex = normalizedObjectKey.LastIndexOf('/');

        return separatorIndex >= 0 && separatorIndex < normalizedObjectKey.Length - 1
            ? normalizedObjectKey[(separatorIndex + 1)..]
            : normalizedObjectKey;
    }

    private static OrderStatus? ParseOrderStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "pending" => OrderStatus.Pending,
            "completed" or "paid" => OrderStatus.Completed,
            "failed" => OrderStatus.Failed,
            "refunded" => OrderStatus.Refunded,
            _ => throw new BadRequestException("Gecersiz siparis status degeri.")
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

    private static string ToPaymentStatusName(PaymentStatusType status)
    {
        return status switch
        {
            PaymentStatusType.Processing => "processing",
            PaymentStatusType.Succeeded => "succeeded",
            PaymentStatusType.Failed => "failed",
            PaymentStatusType.Refunded => "refunded",
            _ => status.ToString()
        };
    }

    private static string ToProductTypeName(ProductType productType)
    {
        return productType == ProductType.Course ? "course" : "digital_file";
    }
}
