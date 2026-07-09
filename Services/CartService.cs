using CraftoraApi.Data;
using CraftoraApi.DTOs.Cart;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CraftoraApi.Services;

public sealed class CartService : ICartService
{
    private readonly AppDbContext _dbContext;

    public CartService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CartResponseDto> AddToCartAsync(Guid userId, AddToCartDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateQuantity(dto.Quantity);

        var productExists = await _dbContext.Products.AnyAsync(product =>
            product.Id == dto.ProductId &&
            product.IsActive == true &&
            product.Status == ProductStatus.Published);
        if (!productExists)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var cartItem = await _dbContext.CartItems.FirstOrDefaultAsync(item =>
            item.UserId == userId &&
            item.ProductId == dto.ProductId);

        if (cartItem is null)
        {
            _dbContext.CartItems.Add(new CartItem
            {
                UserId = userId,
                ProductId = dto.ProductId,
                Quantity = dto.Quantity,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            cartItem.Quantity = (cartItem.Quantity ?? 0) + dto.Quantity;
            cartItem.UpdatedAt = DateTime.UtcNow;
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (IsDuplicatePurchaseException(exception))
        {
            throw new BadRequestException("Bu ürün zaten kütüphanenizde mevcut.");
        }

        return await GetUserCartAsync(userId);
    }

    public async Task<CartResponseDto> UpdateCartItemQuantityAsync(Guid userId, Guid cartItemId, int quantity)
    {
        ValidateQuantity(quantity);

        var cartItem = await _dbContext.CartItems.FirstOrDefaultAsync(item =>
            item.Id == cartItemId &&
            item.UserId == userId);

        if (cartItem is null)
        {
            throw new NotFoundException("Sepet urunu bulunamadi.");
        }

        cartItem.Quantity = quantity;
        cartItem.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return await GetUserCartAsync(userId);
    }

    public async Task RemoveFromCartAsync(Guid userId, Guid cartItemId)
    {
        var cartItem = await _dbContext.CartItems.FirstOrDefaultAsync(item =>
            item.Id == cartItemId &&
            item.UserId == userId);

        if (cartItem is null)
        {
            throw new NotFoundException("Sepet urunu bulunamadi.");
        }

        _dbContext.CartItems.Remove(cartItem);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<CartResponseDto> GetUserCartAsync(Guid userId)
    {
        var cartItems = await _dbContext.CartItems
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item =>
                item.UserId == userId &&
                item.Product.IsActive == true &&
                item.Product.Status == ProductStatus.Published)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync();

        var items = cartItems.Select(MapToResponse).ToList();

        return new CartResponseDto(
            Items: items,
            TotalPrice: items.Sum(item => item.SubTotal));
    }

    public async Task ClearCartAsync(Guid userId)
    {
        var cartItems = await _dbContext.CartItems
            .Where(item => item.UserId == userId)
            .ToListAsync();

        _dbContext.CartItems.RemoveRange(cartItems);
        await _dbContext.SaveChangesAsync();
    }

    private static CartItemResponseDto MapToResponse(CartItem item)
    {
        var quantity = item.Quantity ?? 1;
        var productPrice = item.Product.Price;

        return new CartItemResponseDto(
            Id: item.Id,
            ProductId: item.ProductId,
            ProductName: item.Product.Title,
            ProductPrice: productPrice,
            Quantity: quantity,
            SubTotal: productPrice * quantity);
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity < 1)
        {
            throw new BadRequestException("Miktar 1 veya daha buyuk olmalidir.");
        }
    }

    private static bool IsDuplicatePurchaseException(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
            postgresException.SqlState == "P0001" &&
            postgresException.MessageText.Contains("zaten kutuphanenizde", StringComparison.OrdinalIgnoreCase);
    }
}
