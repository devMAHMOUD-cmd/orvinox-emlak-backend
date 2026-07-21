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

    [Authorize]
    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedMediaAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        var result = await _mediaService.GetSavedMediaAsync(GetCurrentUserId(), page, pageSize);
        return Ok(result);
    }

    [Authorize]
    [HttpGet("liked")]
    public async Task<IActionResult> GetLikedMediaAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        var result = await _mediaService.GetLikedMediaAsync(GetCurrentUserId(), page, pageSize);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetMediaByIdAsync([FromRoute] Guid id)
    {
        var result = await _mediaService.GetMediaByIdAsync(id, TryGetCurrentUserId());
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/likes")]
    public async Task<ActionResult<MediaLikeListResponseDto>> GetMediaLikesAsync(
        [FromRoute] Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        var result = await _mediaService.GetMediaLikesAsync(id, page, pageSize);
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
        var result = await _mediaService.ToggleLikeAsync(id, GetCurrentUserId());
        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/save")]
    public async Task<IActionResult> ToggleSaveAsync([FromRoute] Guid id)
    {
        var result = await _mediaService.ToggleSaveAsync(id, GetCurrentUserId());
        return Ok(result);
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
        var result = await _mediaService.AddCommentAsync(id, GetCurrentUserId(), dto.Text, dto.ParentCommentId);
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
        var result = await _mediaService.DeleteCommentAsync(commentId, GetCurrentUserId());
        return Ok(result);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteMediaAsync([FromRoute] Guid id)
    {
        await _mediaService.DeleteMediaAsync(id, GetCurrentUserId());
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("{id:guid}/view")]
    public async Task<IActionResult> RecordViewAsync([FromRoute] Guid id)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var referrer = Request.Headers.Referer.ToString();

        await _mediaService.RecordViewAsync(
            id,
            TryGetCurrentUserId(),
            HttpContext.Connection.RemoteIpAddress,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            string.IsNullOrWhiteSpace(referrer) ? null : referrer);
        return Ok(new { message = "Goruntulenme kaydedildi." });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Gecersiz kullanici token'i.");
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
