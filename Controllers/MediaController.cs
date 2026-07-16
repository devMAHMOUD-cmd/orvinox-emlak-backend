using System.Security.Claims;
using CraftoraApi.DTOs.Media;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController : ControllerBase
{
    private readonly IMediaService _mediaService;

    public MediaController(IMediaService mediaService)
    {
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
    }

    [AllowAnonymous]
    [HttpGet("feed")]
    public async Task<IActionResult> GetFeedAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _mediaService.GetFeedAsync(TryGetCurrentUserId(), page, pageSize);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("shop/{shopId:guid}")]
    public async Task<IActionResult> GetShopMediaAsync(
        [FromRoute] Guid shopId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _mediaService.GetShopMediaAsync(shopId, page, pageSize);
        return Ok(result);
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpPost]
    public async Task<IActionResult> UploadMediaAsync([FromBody] UploadMediaDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _mediaService.UploadMediaAsync(userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/like")]
    public async Task<IActionResult> ToggleLikeAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _mediaService.ToggleLikeAsync(id, userId);

        return Ok(new { message = "Beğeni durumu güncellendi." });
    }

    [Authorize]
    [HttpPost("{id:guid}/save")]
    public async Task<IActionResult> ToggleSaveAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _mediaService.ToggleSaveAsync(id, userId);

        return Ok(new { message = "Kaydetme durumu güncellendi." });
    }

    [Authorize]
    [EnableRateLimiting("general")]
    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> RecordShareAsync([FromRoute] Guid id)
    {
        var result = await _mediaService.RecordShareAsync(id, GetCurrentUserId());
        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> AddCommentAsync(
        [FromRoute] Guid id,
        [FromBody] CreateMediaCommentDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _mediaService.AddCommentAsync(id, userId, dto.Text, dto.ParentCommentId);

        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/comments")]
    public async Task<IActionResult> GetCommentsAsync(
        [FromRoute] Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediaService.GetCommentsAsync(id, page, pageSize);
        return Ok(result);
    }

    [Authorize]
    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteCommentAsync([FromRoute] Guid commentId)
    {
        var userId = GetCurrentUserId();
        await _mediaService.DeleteCommentAsync(commentId, userId);

        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteMediaAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _mediaService.DeleteMediaAsync(id, userId);

        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("{id:guid}/view")]
    public async Task<IActionResult> RecordViewAsync([FromRoute] Guid id)
    {
        await _mediaService.RecordViewAsync(id, TryGetCurrentUserId());
        return Ok(new { message = "Görüntülenme kaydedildi." });
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
}
