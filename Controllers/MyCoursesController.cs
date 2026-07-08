using System.Security.Claims;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("general")]
[Route("api/my-courses")]
public sealed class MyCoursesController : ControllerBase
{
    private readonly IMyCourseService _myCourseService;

    public MyCoursesController(IMyCourseService myCourseService)
    {
        _myCourseService = myCourseService ?? throw new ArgumentNullException(nameof(myCourseService));
    }

    [HttpGet]
    public async Task<IActionResult> GetMyCoursesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _myCourseService.GetMyCoursesAsync(GetCurrentUserId(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{courseId:guid}")]
    public async Task<IActionResult> GetMyCourseDetailAsync(
        [FromRoute] Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var result = await _myCourseService.GetMyCourseDetailAsync(
            GetCurrentUserId(),
            courseId,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("lessons/{lessonId:guid}/video-url")]
    public async Task<IActionResult> GenerateLessonVideoUrlAsync(
        [FromRoute] Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        var result = await _myCourseService.GenerateLessonVideoUrlAsync(
            GetCurrentUserId(),
            lessonId,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("resources/{resourceId:guid}/download-url")]
    public async Task<IActionResult> GenerateResourceDownloadUrlAsync(
        [FromRoute] Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        var result = await _myCourseService.GenerateResourceDownloadUrlAsync(
            GetCurrentUserId(),
            resourceId,
            cancellationToken);

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
