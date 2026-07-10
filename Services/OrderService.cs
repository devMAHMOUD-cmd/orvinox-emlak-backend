using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.DTOs.Order;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class OrderService : IOrderService
{
    private const string DefaultCurrency = "USD";
    private const decimal PlatformFeeRate = 0.01m;
    private static readonly TimeSpan CheckoutLockTtl = TimeSpan.FromMinutes(3);

    private readonly AppDbContext _dbContext;
    private readonly IPaymentService _paymentService;
    private readonly INotificationService _notificationService;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ICacheService _cacheService;

    public OrderService(
        AppDbContext dbContext,
        IPaymentService paymentService,
        INotificationService notificationService,
        IRabbitMqPublisher rabbitMqPublisher,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        var createdOrders = new List<Order>();
        var successfulOrders = new List<Order>();

        foreach (var cartItem in cartItems)
        {
            var quantity = Math.Max(cartItem.Quantity ?? 1, 1);
            var amount = Math.Round(cartItem.Product.Price * quantity, 2, MidpointRounding.AwayFromZero);
            var platformFee = Math.Round(amount * PlatformFeeRate, 2, MidpointRounding.AwayFromZero);
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
                Amount = amount,
                Currency = currency,
                PlatformFee = platformFee,
                SellerEarnings = sellerEarnings,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

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
                successfulOrders.Add(order);
            }

            createdOrders.Add(order);
        }

        await _dbContext.SaveChangesAsync();

        if (successfulOrders.Count == cartItems.Count)
        {
            _dbContext.CartItems.RemoveRange(cartItems);
            await _dbContext.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        foreach (var order in successfulOrders)
        {
            var product = cartItems.First(item => item.ProductId == order.ProductId).Product;
            await SendOrderNotificationsAsync(order, product, buyer);
            await PublishInvoiceCommandAsync(order, buyer);
        }

        return createdOrders.Select(MapToResponse).ToList();
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
        }
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
            Amount: order.Amount,
            Status: order.Status.ToString(),
            CreatedAt: order.CreatedAt,
            InvoicePdfUrl: order.InvoicePdfUrl);
    }
}
