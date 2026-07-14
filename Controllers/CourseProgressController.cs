using System.Security.Claims;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/course-progress")]
[Authorize]
[EnableRateLimiting("general")]
public sealed class CourseProgressController : ControllerBase
{
    private readonly ICourseProgressService _courseProgressService;

    public CourseProgressController(ICourseProgressService courseProgressService)
    {
        _courseProgressService = courseProgressService ?? throw new ArgumentNullException(nameof(courseProgressService));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProgressAsync([FromBody] UpdateLessonProgressDto dto)
    {
        var userId = GetCurrentUserId();
        await _courseProgressService.UpdateProgressAsync(userId, dto);

        return Ok(new { message = "Ders ilerlemesi guncellendi." });
    }

    [HttpGet("{courseId:guid}")]
    public async Task<IActionResult> GetCourseProgressAsync([FromRoute] Guid courseId)
    {
        var userId = GetCurrentUserId();
        var result = await _courseProgressService.GetCourseProgressAsync(userId, courseId);

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
