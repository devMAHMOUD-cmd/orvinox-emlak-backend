using System.Security.Claims;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/course-quizzes")]
[Authorize]
public sealed class CourseQuizController : ControllerBase
{
    private readonly ICourseQuizService _courseQuizService;

    public CourseQuizController(ICourseQuizService courseQuizService)
    {
        _courseQuizService = courseQuizService ?? throw new ArgumentNullException(nameof(courseQuizService));
    }

    [HttpPost]
    public async Task<IActionResult> AddQuizAsync([FromBody] CreateCourseQuizDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseQuizService.AddQuizAsync(userId, dto);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateQuizAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateCourseQuizDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseQuizService.UpdateQuizAsync(userId, id, dto);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RemoveQuizAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _courseQuizService.RemoveQuizAsync(userId, id);

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
