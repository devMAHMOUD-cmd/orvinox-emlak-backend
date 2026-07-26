using System.Security.Claims;
using CraftoraApi.DTOs;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("upload")]
public sealed class UploadController : ControllerBase
{
    private readonly IUploadService _uploadService;

    public UploadController(IUploadService uploadService)
    {
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
    }

    [HttpPost("public-url")]
    public IActionResult GeneratePublicUploadUrl([FromBody] GeneratePresignedUrlDto dto)
    {
        return Ok(_uploadService.GenerateUploadUrl(GetCurrentUserId(), dto, isPublic: true));
    }

    [HttpPost("private-url")]
    public IActionResult GeneratePrivateUploadUrl([FromBody] GeneratePresignedUrlDto dto)
    {
        return Ok(_uploadService.GenerateUploadUrl(GetCurrentUserId(), dto, isPublic: false));
    }

    [HttpPost("complete")]
    public async Task<IActionResult> CompleteUploadAsync([FromBody] UploadCompleteDto dto)
    {
        await _uploadService.CompleteUploadAsync(
            GetCurrentUserId(),
            dto,
            HttpContext.RequestAborted);

        return Ok(new { message = "Dosya yuklemesi dogrulandi." });
    }

    [HttpDelete("file")]
    public async Task<IActionResult> DeleteFileAsync(
        [FromQuery] string bucketName,
        [FromQuery] string objectKey)
    {
        await _uploadService.DeleteOwnedFileAsync(
            GetCurrentUserId(),
            bucketName,
            objectKey,
            HttpContext.RequestAborted);

        return NoContent();
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
