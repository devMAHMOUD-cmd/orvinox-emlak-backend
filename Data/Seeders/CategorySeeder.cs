using CraftoraApi.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Data.Seeders;

public static class CategorySeeder
{
    private static readonly (string Name, string Slug)[] DefaultCategories =
    [
        ("Software Development", "software-development"),
        ("Growth Marketing", "growth-marketing"),
        ("Design Assets", "design-assets"),
        ("Media & Video", "media-video"),
        ("Education", "education")
    ];

    public static async Task SeedAsync(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var defaultSlugs = DefaultCategories
            .Select(category => category.Slug)
            .ToArray();

        var existingSlugs = await dbContext.Categories
            .Where(category => defaultSlugs.Contains(category.Slug))
            .Select(category => category.Slug)
            .ToListAsync();

        var existingSlugSet = existingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingCategories = DefaultCategories
            .Where(category => !existingSlugSet.Contains(category.Slug))
            .Select(category => new Category
            {
                Name = category.Name,
                Slug = category.Slug,
                ParentId = null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missingCategories.Count == 0)
        {
            return;
        }

        dbContext.Categories.AddRange(missingCategories);
        await dbContext.SaveChangesAsync();
    }
}
