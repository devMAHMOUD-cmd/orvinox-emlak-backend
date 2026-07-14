using System.Security.Claims;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("general")]
public sealed class CourseController : ControllerBase
{
    private readonly ICourseService _courseService;

    public CourseController(ICourseService courseService)
    {
        _courseService = courseService ?? throw new ArgumentNullException(nameof(courseService));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCourseByIdAsync([FromRoute] Guid id)
    {
        var result = await _courseService.GetCourseByIdAsync(id);
        return Ok(result);
    }

    [HttpGet("product/{productId:guid}")]
    public async Task<IActionResult> GetCourseTreeByProductIdAsync([FromRoute] Guid productId)
    {
        var result = await _courseService.GetCourseTreeByProductIdAsync(productId);
        return Ok(result);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateCourseAsync([FromBody] CreateCourseDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseService.CreateCourseAsync(userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCourseAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateCourseDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _courseService.UpdateCourseAsync(userId, id, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCourseAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _courseService.DeleteCourseAsync(userId, id);

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
