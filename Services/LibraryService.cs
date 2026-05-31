using CraftoraApi.Data;
using CraftoraApi.DTOs.Library;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class LibraryService : ILibraryService
{
    private readonly AppDbContext _dbContext;

    public LibraryService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<List<LibraryItemDto>> GetMyLibraryAsync(Guid userId)
    {
        var libraryItems = await _dbContext.UserLibraries
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.LastAccessedAt ?? item.PurchasedAt)
            .ToListAsync();

        return libraryItems.Select(MapToDto).ToList();
    }

    public async Task MarkAsAccessedAsync(Guid userId, Guid productId)
    {
        var libraryItem = await _dbContext.UserLibraries.FirstOrDefaultAsync(item =>
            item.UserId == userId &&
            item.ProductId == productId);

        if (libraryItem is null)
        {
            throw new NotFoundException("Kutuphane urunu bulunamadi.");
        }

        libraryItem.LastAccessedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    private static LibraryItemDto MapToDto(UserLibrary item)
    {
        return new LibraryItemDto(
            Id: item.Id,
            ProductId: item.ProductId,
            ProductTitle: item.Product.Title,
            CoverImageUrl: item.Product.CoverImageUrl,
            PurchasedAt: item.PurchasedAt,
            LastAccessedAt: item.LastAccessedAt);
    }
}
