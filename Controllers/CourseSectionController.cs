using System.Security.Claims;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/course-sections")]
[Authorize]
[EnableRateLimiting("general")]
public sealed class CourseSectionController : ControllerBase
{
    private readonly ICourseSectionService _courseSectionService;

    public CourseSectionController(ICourseSectionService courseSectionService)
    {
        _courseSectionService = courseSectionService ?? throw new ArgumentNullException(nameof(courseSectionService));
    }

    [HttpPost]
    public async Task<IActionResult> CreateSectionAsync([FromBody] CreateCourseSectionDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseSectionService.CreateSectionAsync(userId, dto);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateSectionAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateCourseSectionDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseSectionService.UpdateSectionAsync(userId, id, dto);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSectionAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _courseSectionService.DeleteSectionAsync(userId, id);

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
