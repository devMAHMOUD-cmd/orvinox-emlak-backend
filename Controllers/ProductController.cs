using System.Security.Claims;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Product;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProductController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly AppDbContext _dbContext;

    public ProductController(
        IProductService productService,
        AppDbContext dbContext)
    {
        _productService = productService ?? throw new ArgumentNullException(nameof(productService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateProductAsync([FromBody] CreateProductDto dto)
    {
        var userId = GetCurrentUserId();
        var shop = await GetCurrentUserShopAsync(userId);

        var result = await _productService.CreateProductAsync(shop.Id, dto);
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetFilteredProductsAsync(
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? shopId,
        [FromQuery] ProductStatus? status,
        [FromQuery] bool includeAllStatuses = false,
        [FromQuery] int page = 1,
        [FromQuery] int size = 10)
    {
        var canViewNonPublished = shopId.HasValue && await CurrentUserOwnsShopAsync(shopId.Value);
        var effectiveStatus = canViewNonPublished ? status : ProductStatus.Published;
        var effectiveIncludeAllStatuses = includeAllStatuses && canViewNonPublished;

        var result = await _productService.GetFilteredProductsAsync(
            categoryId,
            shopId,
            effectiveStatus,
            effectiveIncludeAllStatuses,
            page,
            size);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProductByIdAsync([FromRoute] Guid id)
    {
        var result = await _productService.GetProductByIdAsync(id);
        return Ok(result);
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProductAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateProductDto dto)
    {
        var userId = GetCurrentUserId();
        var shop = await GetCurrentUserShopAsync(userId);

        var result = await _productService.UpdateProductAsync(id, shop.Id, dto);
        return Ok(result);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDeleteProductAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        var shop = await GetCurrentUserShopAsync(userId);

        await _productService.SoftDeleteProductAsync(id, shop.Id);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Geçersiz kullanıcı token'ı.");
        }

        return userId;
    }

    private Guid? TryGetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId)
            ? userId
            : null;
    }

    private async Task<bool> CurrentUserOwnsShopAsync(Guid shopId)
    {
        var userId = TryGetCurrentUserId();
        if (!userId.HasValue)
        {
            return false;
        }

        return await _dbContext.Shops.AnyAsync(shop =>
            shop.Id == shopId &&
            shop.UserId == userId.Value &&
            shop.IsActive == true);
    }

    private async Task<Shop> GetCurrentUserShopAsync(Guid userId)
    {
        var shop = await _dbContext.Shops.FirstOrDefaultAsync(shop => shop.UserId == userId && shop.IsActive == true);

        if (shop is null)
        {
            throw new BadRequestException("Ürün işlemi için önce bir mağaza oluşturmalısınız.");
        }

        return shop;
    }
}
