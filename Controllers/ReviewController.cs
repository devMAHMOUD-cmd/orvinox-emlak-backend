using System.Security.Claims;
using CraftoraApi.DTOs.Interaction;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/reviews")]
[EnableRateLimiting("general")]
public sealed class ReviewController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewController(IReviewService reviewService)
    {
        _reviewService = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AddReviewAsync([FromBody] CreateReviewDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _reviewService.AddReviewAsync(userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateReviewAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateReviewDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _reviewService.UpdateReviewAsync(id, userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteReviewAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _reviewService.DeleteReviewAsync(id, userId);

        return NoContent();
    }

    [Authorize]
    [HttpPost("{id:guid}/reply")]
    public async Task<IActionResult> ReplyToReviewAsync(
        [FromRoute] Guid id,
        [FromBody] ReplyReviewDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _reviewService.ReplyToReviewAsync(id, userId, dto);

        return Ok(result);
    }

    [HttpGet("product/{productId:guid}")]
    public async Task<IActionResult> GetProductReviewsAsync([FromRoute] Guid productId)
    {
        var result = await _reviewService.GetProductReviewsAsync(productId);
        return Ok(result);
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
}
