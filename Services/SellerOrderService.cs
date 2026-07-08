using CraftoraApi.Data;
using CraftoraApi.DTOs.Order;
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

    public SellerOrderService(
        AppDbContext dbContext,
        IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
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
        var totalRevenue = await query
            .Where(order => order.Status == OrderStatus.Completed)
            .SumAsync(order => order.Amount, cancellationToken);
        var pendingAmount = await query
            .Where(order => order.Status == OrderStatus.Pending)
            .SumAsync(order => order.Amount, cancellationToken);
        var averageOrderValue = paidOrders == 0
            ? 0
            : Math.Round(totalRevenue / paidOrders, 2, MidpointRounding.AwayFromZero);

        return new SellerOrderSummaryDto(
            TotalOrders: totalOrders,
            PaidOrders: paidOrders,
            FailedOrders: failedOrders,
            RefundedOrders: refundedOrders,
            TotalRevenue: totalRevenue,
            PendingAmount: pendingAmount,
            AverageOrderValue: averageOrderValue);
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
            PaidAt: order.Payment?.Status == PaymentStatusType.Succeeded ? order.Payment.CreatedAt : null,
            HasProductFile: !string.IsNullOrWhiteSpace(order.Product.FileUrl),
            ProductFileName: GetFileName(order.Product.FileUrl),
            InvoicePdfUrl: order.InvoicePdfUrl);
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
            PaidAt: order.Payment?.Status == PaymentStatusType.Succeeded ? order.Payment.CreatedAt : null,
            HasProductFile: !string.IsNullOrWhiteSpace(order.Product.FileUrl),
            ProductFileName: GetFileName(order.Product.FileUrl),
            InvoicePdfUrl: order.InvoicePdfUrl,
            PaymentProvider: order.Payment?.PaymentProvider,
            ProviderTransactionId: order.Payment?.ProviderTransactionId,
            PaymentErrorMessage: order.Payment?.ErrorMessage,
            AccessStatus: hasLibraryAccess ? "delivered" : "pending",
            CourseEnrollmentStatus: order.Product.Type == ProductType.Course
                ? hasLibraryAccess ? "enrolled" : "not_enrolled"
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
