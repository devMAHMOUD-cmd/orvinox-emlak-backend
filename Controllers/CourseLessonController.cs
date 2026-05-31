using System.Security.Claims;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/course-lessons")]
[Authorize]
public sealed class CourseLessonController : ControllerBase
{
    private readonly ICourseLessonService _courseLessonService;

    public CourseLessonController(ICourseLessonService courseLessonService)
    {
        _courseLessonService = courseLessonService ?? throw new ArgumentNullException(nameof(courseLessonService));
    }

    [HttpPost]
    public async Task<IActionResult> CreateLessonAsync([FromBody] CreateCourseLessonDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseLessonService.CreateLessonAsync(userId, dto);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateLessonAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateCourseLessonDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseLessonService.UpdateLessonAsync(userId, id, dto);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteLessonAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _courseLessonService.DeleteLessonAsync(userId, id);

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
