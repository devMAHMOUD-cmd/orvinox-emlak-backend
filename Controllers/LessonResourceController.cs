using System.Security.Claims;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/lesson-resources")]
[Authorize]
[EnableRateLimiting("general")]
public sealed class LessonResourceController : ControllerBase
{
    private readonly ILessonResourceService _lessonResourceService;

    public LessonResourceController(ILessonResourceService lessonResourceService)
    {
        _lessonResourceService = lessonResourceService ?? throw new ArgumentNullException(nameof(lessonResourceService));
    }

    [HttpPost]
    public async Task<IActionResult> AddResourceAsync([FromBody] CreateLessonResourceDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _lessonResourceService.AddResourceAsync(userId, dto);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RemoveResourceAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _lessonResourceService.RemoveResourceAsync(userId, id);

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
