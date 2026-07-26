using CraftoraApi.Data;
using CraftoraApi.DTOs.Coupon;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CraftoraApi.Services;

public sealed class CouponService : ICouponService
{
    private const string PercentDiscountType = "percent";
    private const string FixedDiscountType = "fixed";

    private readonly AppDbContext _dbContext;

    public CouponService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CouponResponseDto> CreateCouponAsync(Guid userId, CreateCouponDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var discountType = NormalizeDiscountType(dto.DiscountType);
        ValidateDiscount(discountType, dto.DiscountValue);

        var product = await _dbContext.Products
            .Include(product => product.Shop)
            .FirstOrDefaultAsync(product =>
                product.Id == dto.ProductId &&
                product.IsActive == true);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        await EnsureCanCreateCouponAsync(userId, product.Shop.UserId);

        var code = NormalizeCode(dto.Code);
        var couponExists = await _dbContext.Coupons.AnyAsync(coupon =>
            coupon.ProductId == dto.ProductId &&
            coupon.Code.ToLower() == code.ToLower());

        if (couponExists)
        {
            throw new ConflictException("Bu urun icin ayni kupon kodu zaten mevcut.");
        }

        var coupon = new Coupon
        {
            ProductId = product.Id,
            ShopId = product.ShopId,
            Code = code,
            DiscountType = discountType,
            DiscountValue = dto.DiscountValue,
            MinimumCartAmount = dto.MinimumCartAmount ?? 0,
            MaxUses = dto.UsageLimit,
            UsedCount = 0,
            StartsAt = DateTime.UtcNow,
            ExpiresAt = dto.ExpirationDate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(coupon);
    }

    public async Task<ValidateCouponResponseDto> ValidateCouponAsync(Guid userId, ValidateCouponRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var cartTotal = Math.Max(dto.CartTotalAmount, 0);
        var code = NormalizeCode(dto.Code);

        var coupon = await _dbContext.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(coupon =>
                coupon.ProductId == dto.ProductId &&
                coupon.Code.ToLower() == code.ToLower() &&
                coupon.IsActive == true);

        if (coupon is null)
        {
            return Invalid(cartTotal, "Kupon bulunamadi veya aktif degil.");
        }

        var now = DateTime.UtcNow;
        if (coupon.StartsAt.HasValue && coupon.StartsAt.Value > now)
        {
            return Invalid(cartTotal, "Kupon henuz kullanima acik degil.");
        }

        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt.Value < now)
        {
            return Invalid(cartTotal, "Kuponun suresi dolmus.");
        }

        if (coupon.MaxUses.HasValue && (coupon.UsedCount ?? 0) >= coupon.MaxUses.Value)
        {
            return Invalid(cartTotal, "Kupon kullanim limiti dolmus.");
        }

        var alreadyUsed = await _dbContext.CouponUses.AnyAsync(couponUse =>
            couponUse.CouponId == coupon.Id &&
            couponUse.UserId == userId);

        if (alreadyUsed)
        {
            return Invalid(cartTotal, "Bu kuponu daha once kullandiniz.");
        }

        var minimumCartAmount = coupon.MinimumCartAmount ?? 0;
        if (cartTotal < minimumCartAmount)
        {
            return Invalid(cartTotal, $"Bu kupon icin minimum sepet tutari {minimumCartAmount:0.##} olmalidir.");
        }

        var discountAmount = CalculateDiscountAmount(coupon, cartTotal);
        var finalTotal = Math.Max(cartTotal - discountAmount, 0);

        return new ValidateCouponResponseDto(
            IsValid: true,
            ErrorMessage: null,
            DiscountAmount: discountAmount,
            FinalTotal: finalTotal);
    }

    public async Task RecordCouponUsageAsync(Guid userId, Guid couponId, Guid orderId)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        var coupon = await _dbContext.Coupons
            .FromSqlInterpolated($"""
                SELECT *
                FROM coupons
                WHERE id = {couponId} AND is_active = true
                FOR UPDATE
                """)
            .FirstOrDefaultAsync();

        if (coupon is null)
        {
            throw new NotFoundException("Kupon bulunamadi.");
        }

        var orderExists = await _dbContext.Orders.AnyAsync(order =>
            order.Id == orderId &&
            order.BuyerId == userId);
        if (!orderExists)
        {
            throw new NotFoundException("Siparis bulunamadi.");
        }

        var alreadyUsed = await _dbContext.CouponUses.AnyAsync(couponUse =>
            couponUse.CouponId == couponId &&
            couponUse.UserId == userId);
        if (alreadyUsed)
        {
            throw new ConflictException("Bu kupon daha once kullanilmis.");
        }

        if (coupon.MaxUses.HasValue && (coupon.UsedCount ?? 0) >= coupon.MaxUses.Value)
        {
            throw new ConflictException("Kupon kullanim limiti dolmus.");
        }

        _dbContext.CouponUses.Add(new CouponUse
        {
            CouponId = couponId,
            UserId = userId,
            OrderId = orderId,
            UsedAt = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException exception) when (IsDuplicateCouponUse(exception))
        {
            throw new ConflictException("Bu kupon daha once kullanilmis.");
        }
    }

    public async Task<CheckoutCouponResult> ResolveForCheckoutAsync(
        Guid userId,
        Guid productId,
        string code,
        decimal subtotalAmount)
    {
        var normalizedCode = NormalizeCode(code);
        var coupon = await _dbContext.Coupons
            .FromSqlInterpolated($"""
                SELECT *
                FROM coupons
                WHERE product_id = {productId}
                  AND code = {normalizedCode}
                  AND is_active = true
                FOR UPDATE
                """)
            .FirstOrDefaultAsync();

        if (coupon is null)
        {
            throw new BadRequestException("Kupon bulunamadi veya aktif degil.");
        }

        var now = DateTime.UtcNow;
        if (coupon.StartsAt.HasValue && coupon.StartsAt.Value > now)
        {
            throw new BadRequestException("Kupon henuz kullanima acik degil.");
        }

        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt.Value < now)
        {
            throw new BadRequestException("Kuponun suresi dolmus.");
        }

        if (coupon.MaxUses.HasValue && (coupon.UsedCount ?? 0) >= coupon.MaxUses.Value)
        {
            throw new BadRequestException("Kupon kullanim limiti dolmus.");
        }

        var alreadyUsed = await _dbContext.CouponUses.AnyAsync(couponUse =>
            couponUse.CouponId == coupon.Id &&
            couponUse.UserId == userId);
        if (alreadyUsed)
        {
            throw new BadRequestException("Bu kuponu daha once kullandiniz.");
        }

        if (subtotalAmount < (coupon.MinimumCartAmount ?? 0))
        {
            throw new BadRequestException(
                $"Bu kupon icin minimum sepet tutari {coupon.MinimumCartAmount ?? 0:0.##} olmalidir.");
        }

        var discountAmount = CalculateDiscountAmount(coupon, subtotalAmount);
        return new CheckoutCouponResult(
            coupon.Id,
            discountAmount,
            Math.Max(subtotalAmount - discountAmount, 0));
    }

    public void AddUsage(Guid userId, Guid couponId, Guid orderId)
    {
        _dbContext.CouponUses.Add(new CouponUse
        {
            CouponId = couponId,
            UserId = userId,
            OrderId = orderId,
            UsedAt = DateTime.UtcNow
        });
    }

    private async Task EnsureCanCreateCouponAsync(Guid userId, Guid shopOwnerId)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user =>
                user.Id == userId &&
                user.IsActive == true);

        if (user is null)
        {
            throw new UnauthorizedException("Gecersiz kullanici.");
        }

        if (user.Role != UserRole.Admin && shopOwnerId != userId)
        {
            throw new ForbiddenException("Bu urun icin kupon olusturma yetkiniz yok.");
        }
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BadRequestException("Kupon kodu zorunludur.");
        }

        return code.Trim().ToUpperInvariant();
    }

    private static string NormalizeDiscountType(string discountType)
    {
        if (string.IsNullOrWhiteSpace(discountType))
        {
            throw new BadRequestException("Indirim tipi zorunludur.");
        }

        return discountType.Trim().ToLowerInvariant();
    }

    private static void ValidateDiscount(string discountType, decimal discountValue)
    {
        if (discountType is not PercentDiscountType and not FixedDiscountType)
        {
            throw new BadRequestException("Indirim tipi 'percent' veya 'fixed' olmalidir.");
        }

        if (discountValue <= 0)
        {
            throw new BadRequestException("Indirim degeri 0'dan buyuk olmalidir.");
        }

        if (discountType == PercentDiscountType && discountValue > 100)
        {
            throw new BadRequestException("Yuzde indirim 100'den buyuk olamaz.");
        }
    }

    private static decimal CalculateDiscountAmount(Coupon coupon, decimal cartTotal)
    {
        var discountAmount = coupon.DiscountType == PercentDiscountType
            ? cartTotal * coupon.DiscountValue / 100
            : coupon.DiscountValue;

        return Math.Min(Math.Round(discountAmount, 2), cartTotal);
    }

    private static ValidateCouponResponseDto Invalid(decimal cartTotal, string errorMessage)
    {
        return new ValidateCouponResponseDto(
            IsValid: false,
            ErrorMessage: errorMessage,
            DiscountAmount: 0,
            FinalTotal: cartTotal);
    }

    private static CouponResponseDto MapToResponse(Coupon coupon)
    {
        return new CouponResponseDto(
            Id: coupon.Id,
            Code: coupon.Code,
            DiscountType: coupon.DiscountType,
            DiscountValue: coupon.DiscountValue,
            ExpirationDate: coupon.ExpiresAt,
            IsActive: coupon.IsActive ?? false);
    }

    private static bool IsDuplicateCouponUse(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
            postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgresException.ConstraintName == "coupon_uses_coupon_id_user_id_key";
    }
}
