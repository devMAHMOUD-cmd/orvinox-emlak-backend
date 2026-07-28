using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.DTOs.Order;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services.Discovery;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace CraftoraApi.Services;

public sealed class OrderService : IOrderService
{
    private const string DefaultCurrency = "USD";
    private static readonly TimeSpan CheckoutLockTtl = TimeSpan.FromMinutes(3);

    private readonly AppDbContext _dbContext;
    private readonly IPaymentService _paymentService;
    private readonly INotificationService _notificationService;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ICacheService _cacheService;
    private readonly IAnalyticsEventService _analyticsEventService;
    private readonly ISellerNotificationPreferenceService _sellerNotificationPreferenceService;
    private readonly IGamificationService _gamificationService;
    private readonly ICouponService _couponService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        AppDbContext dbContext,
        IPaymentService paymentService,
        INotificationService notificationService,
        IRabbitMqPublisher rabbitMqPublisher,
        ICacheService cacheService,
        IAnalyticsEventService analyticsEventService,
        ISellerNotificationPreferenceService sellerNotificationPreferenceService,
        IGamificationService gamificationService,
        ICouponService couponService,
        ILogger<OrderService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _analyticsEventService = analyticsEventService ?? throw new ArgumentNullException(nameof(analyticsEventService));
        _sellerNotificationPreferenceService = sellerNotificationPreferenceService ?? throw new ArgumentNullException(nameof(sellerNotificationPreferenceService));
        _gamificationService = gamificationService ?? throw new ArgumentNullException(nameof(gamificationService));
        _couponService = couponService ?? throw new ArgumentNullException(nameof(couponService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<OrderResponseDto>> CheckoutCartAsync(Guid buyerId, CheckoutRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var buyer = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == buyerId);

        if (buyer is null)
        {
            throw new UnauthorizedException("Geçersiz kullanıcı.");
        }

        var checkoutLockKey = GetCheckoutLockKey(buyerId);
        var checkoutLockValue = Guid.NewGuid().ToString("N");
        var lockAcquired = await TryAcquireCheckoutLockAsync(checkoutLockKey, checkoutLockValue);

        if (!lockAcquired)
        {
            throw new BadRequestException("Devam eden bir odeme isleminiz var.");
        }

        try
        {
        var cartItems = await _dbContext.CartItems
            .Include(item => item.Product)
            .ThenInclude(product => product.Shop)
            .Where(item =>
                item.UserId == buyerId &&
                item.Product.IsActive == true &&
                item.Product.Status == ProductStatus.Published &&
                item.Product.Shop.IsActive == true)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            throw new BadRequestException("Sepetiniz boş.");
        }

        if (cartItems.Any(cartItem => cartItem.Product.Shop.UserId == buyerId))
        {
            throw new BadRequestException("Kendi urununuzu satin alamazsiniz.");
        }

        var cartProductIds = cartItems
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();
        var alreadyOwnedProductIds = await _dbContext.UserLibraries
            .AsNoTracking()
            .Where(item =>
                item.UserId == buyerId &&
                cartProductIds.Contains(item.ProductId))
            .Select(item => item.ProductId)
            .ToListAsync();

        if (alreadyOwnedProductIds.Count > 0)
        {
            throw new ConflictException(
                "Sepetinizde zaten kutuphanenizde bulunan bir urun var. Sepeti yenileyip tekrar deneyin.");
        }

        var couponCodes = (request.Coupons ?? [])
            .ToDictionary(coupon => coupon.ProductId, coupon => coupon.Code);
        var unknownCouponProductId = couponCodes.Keys
            .FirstOrDefault(productId => !cartProductIds.Contains(productId));
        if (unknownCouponProductId != Guid.Empty)
        {
            throw new BadRequestException("Kupon yalnizca sepetinizdeki urunlere uygulanabilir.");
        }

        var commissionSnapshots = new Dictionary<Guid, CommissionSnapshot>();
        foreach (var shopId in cartItems.Select(item => item.Product.ShopId).Distinct())
        {
            commissionSnapshots[shopId] = await GetCommissionSnapshotAsync(shopId);
        }

        var createdOrders = new List<Order>();
        var successfulOrders = new List<(Order Order, Product Product)>();

        foreach (var cartItem in cartItems)
        {
            // A failed item must not roll back earlier successful purchases.
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var quantity = Math.Max(cartItem.Quantity ?? 1, 1);
            var subtotalAmount = Math.Round(
                cartItem.Product.Price * quantity,
                2,
                MidpointRounding.AwayFromZero);
            CheckoutCouponResult? couponResult = null;
            if (couponCodes.TryGetValue(cartItem.ProductId, out var couponCode))
            {
                couponResult = await _couponService.ResolveForCheckoutAsync(
                    buyerId,
                    cartItem.ProductId,
                    couponCode,
                    subtotalAmount);
            }

            var discountAmount = couponResult?.DiscountAmount ?? 0;
            var amount = couponResult?.FinalTotal ?? subtotalAmount;
            var commissionSnapshot = commissionSnapshots[cartItem.Product.ShopId];
            var platformFee = Math.Round(amount * commissionSnapshot.CommissionRate, 2, MidpointRounding.AwayFromZero);
            var sellerEarnings = amount - platformFee;
            var currency = string.IsNullOrWhiteSpace(cartItem.Product.Currency)
                ? DefaultCurrency
                : cartItem.Product.Currency;

            var order = new Order
            {
                Id = Guid.NewGuid(),
                BuyerId = buyerId,
                ProductId = cartItem.ProductId,
                ShopId = cartItem.Product.ShopId,
                OrderNumber = await GenerateOrderNumberAsync(),
                SubtotalAmount = subtotalAmount,
                DiscountAmount = discountAmount,
                Amount = amount,
                Currency = currency,
                PlatformFee = platformFee,
                SubscriptionPlanId = commissionSnapshot.PlanId,
                CommissionRate = commissionSnapshot.CommissionRate,
                SellerEarnings = sellerEarnings,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            await _analyticsEventService.TrackCheckoutStartedAsync(cartItem.ProductId, buyerId);

            // TODO: Pass a durable per-cart-item provider idempotency key when real payments are introduced.
            var paymentResult = await _paymentService.ProcessPaymentAsync(
                amount,
                currency ?? DefaultCurrency,
                request.CardNumber);

            var paymentStatus = paymentResult.IsSuccess
                ? PaymentStatusType.Succeeded
                : PaymentStatusType.Failed;

            _dbContext.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                PaymentProvider = "mock",
                ProviderTransactionId = paymentResult.IsSuccess ? paymentResult.TransactionId : null,
                GrossAmount = amount,
                PlatformFeeAmount = platformFee,
                SubscriptionPlanId = commissionSnapshot.PlanId,
                CommissionRate = commissionSnapshot.CommissionRate,
                NetEarnings = sellerEarnings,
                Status = paymentStatus,
                ErrorMessage = paymentResult.IsSuccess ? null : paymentResult.ErrorMessage,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            if (!paymentResult.IsSuccess)
            {
                order.Status = OrderStatus.Failed;
                order.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                order.Status = OrderStatus.Completed;
                order.UpdatedAt = DateTime.UtcNow;
                if (couponResult is not null)
                {
                    _couponService.AddUsage(buyerId, couponResult.CouponId, order.Id);
                }
                _dbContext.CartItems.Remove(cartItem);
                successfulOrders.Add((order, cartItem.Product));
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            createdOrders.Add(order);

        }

        foreach (var (order, product) in successfulOrders)
        {
            await RunPostCheckoutActionsSafelyAsync(order, product, buyer);
        }

        return createdOrders.Select(MapToResponse).ToList();
        }
        finally
        {
            await ReleaseCheckoutLockSafelyAsync(checkoutLockKey, checkoutLockValue);
        }
    }

    public async Task<OrderResponseDto> CheckoutDirectAsync(Guid buyerId, DirectCheckoutRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var buyer = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == buyerId);

        if (buyer is null)
        {
            throw new UnauthorizedException("Gecersiz kullanici.");
        }

        var checkoutLockKey = GetCheckoutLockKey(buyerId);
        var checkoutLockValue = Guid.NewGuid().ToString("N");
        var lockAcquired = await TryAcquireCheckoutLockAsync(checkoutLockKey, checkoutLockValue);

        if (!lockAcquired)
        {
            throw new BadRequestException("Devam eden bir odeme isleminiz var.");
        }

        try
        {
            var product = await _dbContext.Products
                .Include(item => item.Shop)
                .FirstOrDefaultAsync(item =>
                    item.Id == request.ProductId &&
                    item.IsActive == true &&
                    item.Status == ProductStatus.Published &&
                    item.Shop.IsActive == true);

            if (product is null)
            {
                throw new NotFoundException("Urun bulunamadi.");
            }

            if (product.Shop.UserId == buyerId)
            {
                throw new BadRequestException("Kendi urununuzu satin alamazsiniz.");
            }

            var alreadyOwned = await _dbContext.UserLibraries
                .AsNoTracking()
                .AnyAsync(item => item.UserId == buyerId && item.ProductId == product.Id);

            if (alreadyOwned)
            {
                throw new BadRequestException("Bu urun zaten kutuphanenizde mevcut.");
            }

            var subtotalAmount = Math.Round(product.Price, 2, MidpointRounding.AwayFromZero);
            var commissionSnapshot = await GetCommissionSnapshotAsync(product.ShopId);
            var currency = string.IsNullOrWhiteSpace(product.Currency)
                ? DefaultCurrency
                : product.Currency;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            CheckoutCouponResult? couponResult = null;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                couponResult = await _couponService.ResolveForCheckoutAsync(
                    buyerId,
                    product.Id,
                    request.CouponCode,
                    subtotalAmount);
            }

            var discountAmount = couponResult?.DiscountAmount ?? 0;
            var amount = couponResult?.FinalTotal ?? subtotalAmount;
            var platformFee = Math.Round(amount * commissionSnapshot.CommissionRate, 2, MidpointRounding.AwayFromZero);
            var sellerEarnings = amount - platformFee;

            var order = new Order
            {
                Id = Guid.NewGuid(),
                BuyerId = buyerId,
                ProductId = product.Id,
                ShopId = product.ShopId,
                OrderNumber = await GenerateOrderNumberAsync(),
                SubtotalAmount = subtotalAmount,
                DiscountAmount = discountAmount,
                Amount = amount,
                Currency = currency,
                PlatformFee = platformFee,
                SubscriptionPlanId = commissionSnapshot.PlanId,
                CommissionRate = commissionSnapshot.CommissionRate,
                SellerEarnings = sellerEarnings,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            await _analyticsEventService.TrackCheckoutStartedAsync(product.Id, buyerId);

            // TODO: Send a durable direct-checkout idempotency key to the real payment provider.
            var paymentResult = await _paymentService.ProcessPaymentAsync(amount, currency, request.CardNumber);
            var paymentStatus = paymentResult.IsSuccess
                ? PaymentStatusType.Succeeded
                : PaymentStatusType.Failed;

            _dbContext.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                PaymentProvider = "mock",
                ProviderTransactionId = paymentResult.IsSuccess ? paymentResult.TransactionId : null,
                GrossAmount = amount,
                PlatformFeeAmount = platformFee,
                SubscriptionPlanId = commissionSnapshot.PlanId,
                CommissionRate = commissionSnapshot.CommissionRate,
                NetEarnings = sellerEarnings,
                Status = paymentStatus,
                ErrorMessage = paymentResult.IsSuccess ? null : paymentResult.ErrorMessage,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            order.Status = paymentResult.IsSuccess ? OrderStatus.Completed : OrderStatus.Failed;
            order.UpdatedAt = DateTime.UtcNow;
            if (paymentResult.IsSuccess && couponResult is not null)
            {
                _couponService.AddUsage(buyerId, couponResult.CouponId, order.Id);
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            if (paymentResult.IsSuccess)
            {
                await RunPostCheckoutActionsSafelyAsync(order, product, buyer);
            }

            return MapToResponse(order);
        }
        finally
        {
            await ReleaseCheckoutLockSafelyAsync(checkoutLockKey, checkoutLockValue);
        }
    }

    public async Task<List<OrderResponseDto>> GetMyOrdersAsync(Guid buyerId)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync();

        return orders.Select(MapToResponse).ToList();
    }

    private async Task<bool> TryAcquireCheckoutLockAsync(string key, string value)
    {
        try
        {
            return await _cacheService.TryAcquireLockAsync(key, value, CheckoutLockTtl);
        }
        catch
        {
            throw new BadRequestException("Odeme islemi su anda baslatilamiyor. Lutfen tekrar deneyin.");
        }
    }

    private async Task ReleaseCheckoutLockSafelyAsync(string key, string value)
    {
        try
        {
            await _cacheService.ReleaseLockAsync(key, value);
        }
        catch
        {
            // Lock has a short TTL; payment result should not fail after transaction commit because Redis release failed.
        }
    }

    private static string GetCheckoutLockKey(Guid buyerId)
    {
        return $"checkout:lock:user:{buyerId:D}";
    }

    private async Task<CommissionSnapshot> GetCommissionSnapshotAsync(Guid shopId)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT plan_id, commission_rate
            FROM public.get_shop_commission_snapshot(@shop_id)
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "shop_id";
        parameter.Value = shopId;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new ConflictException("Magazanin aktif abonelik plani bulunamadi.");
        }

        return new CommissionSnapshot(reader.GetGuid(0), reader.GetDecimal(1));
    }

    private async Task SendOrderNotificationsAsync(Order order, Product product, User buyer)
    {
        await _notificationService.SendNotificationAsync(
            buyer.Id,
            "Siparişiniz tamamlandı",
            $"{product.Title} ürününüz kütüphanenize eklendi.",
            NotificationType.NewOrder,
            order.Id);

        if (product.Shop.UserId != buyer.Id)
        {
            await _notificationService.SendNotificationAsync(
                product.Shop.UserId,
                "Yeni sipariş aldınız",
                $"{buyer.FullName ?? buyer.Email}, {product.Title} ürününü satın aldı.",
                NotificationType.NewOrder,
                order.Id);

            await PublishSellerOrderEmailIfEnabledAsync(order, product, buyer);
        }
    }

    private async Task RunPostCheckoutActionsSafelyAsync(Order order, Product product, User buyer)
    {
        await TryRunPostCheckoutActionAsync(
            order.Id,
            "analytics",
            () => _analyticsEventService.TrackPurchaseCompletedAsync(order.Id, buyer.Id));

        await TryRunPostCheckoutActionAsync(
            order.Id,
            "discovery-cache",
            () => Task.WhenAll(
                _cacheService.RemoveAsync(DiscoveryCacheKeys.ReelsSnapshot(buyer.Id)),
                _cacheService.RemoveAsync(
                    DiscoveryCacheKeys.ProductSnapshot(buyer.Id, "product")),
                _cacheService.RemoveAsync(
                    DiscoveryCacheKeys.ProductSnapshot(buyer.Id, "course")),
                _cacheService.RemoveAsync(DiscoveryCacheKeys.MixedSnapshot(buyer.Id))));

        await TryRunPostCheckoutActionAsync(
            order.Id,
            "gamification",
            () => _gamificationService.AwardPointsAsync(
                buyer.Id,
                "purchase_product",
                5m,
                order.Id,
                preventDuplicate: true));

        await TryRunPostCheckoutActionAsync(
            order.Id,
            "notifications",
            () => SendOrderNotificationsAsync(order, product, buyer));

        await TryRunPostCheckoutActionAsync(
            order.Id,
            "invoice",
            () => PublishInvoiceCommandAsync(order, buyer));
    }

    private async Task TryRunPostCheckoutActionAsync(
        Guid orderId,
        string actionName,
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Post-checkout action failed without changing the completed order. OrderId: {OrderId}, Action: {Action}",
                orderId,
                actionName);
        }
    }

    private async Task PublishSellerOrderEmailIfEnabledAsync(Order order, Product product, User buyer)
    {
        var sellerUserId = product.Shop.UserId;
        if (!await _sellerNotificationPreferenceService.AreOrderEmailsEnabledAsync(sellerUserId))
        {
            return;
        }

        var seller = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == sellerUserId);

        if (seller is null || string.IsNullOrWhiteSpace(seller.Email))
        {
            return;
        }

        await _rabbitMqPublisher.PublishSendEmailCommand(new SendEmailCommand(
            To: seller.Email,
            Subject: "Yeni sipariş aldınız",
            Body: SellerNotificationPreferenceService.BuildOrderEmailBody(
                product.Shop.ShopName,
                product.Title,
                order.OrderNumber,
                buyer.FullName ?? buyer.Email,
                order.Amount,
                order.Currency,
                order.Id),
            IsHtml: true));
    }

    private async Task PublishInvoiceCommandAsync(Order order, User buyer)
    {
        await _rabbitMqPublisher.PublishGenerateInvoiceCommand(new GenerateInvoiceCommand(
            OrderId: order.Id,
            UserId: buyer.Id,
            Amount: order.Amount,
            CustomerName: buyer.FullName ?? buyer.Email,
            CustomerEmail: buyer.Email));
    }

    private async Task<string> GenerateOrderNumberAsync()
    {
        string orderNumber;

        do
        {
            orderNumber = $"ORD-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        }
        while (await _dbContext.Orders.AnyAsync(order => order.OrderNumber == orderNumber));

        return orderNumber;
    }

    private static OrderResponseDto MapToResponse(Order order)
    {
        return new OrderResponseDto(
            Id: order.Id,
            OrderNumber: order.OrderNumber,
            SubtotalAmount: order.SubtotalAmount,
            DiscountAmount: order.DiscountAmount,
            Amount: order.Amount,
            Status: order.Status.ToString(),
            CreatedAt: order.CreatedAt,
            InvoicePdfUrl: order.InvoicePdfUrl);
    }

    private sealed record CommissionSnapshot(Guid PlanId, decimal CommissionRate);
}
