using System.Security.Claims;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("general")]
[Route("api/courses")]
public sealed class CoursesController : ControllerBase
{
    private readonly IPublicCourseService _publicCourseService;

    public CoursesController(IPublicCourseService publicCourseService)
    {
        _publicCourseService = publicCourseService ?? throw new ArgumentNullException(nameof(publicCourseService));
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeaturedCoursesAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _publicCourseService.GetFeaturedCoursesAsync(
            GetOptionalCurrentUserId(),
            page,
            pageSize,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetCoursesAsync(
        [FromQuery] string? query,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? level,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _publicCourseService.GetCoursesAsync(
            GetOptionalCurrentUserId(),
            query,
            categoryId,
            level,
            minPrice,
            maxPrice,
            sort,
            page,
            pageSize,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{courseId:guid}/public")]
    public async Task<IActionResult> GetPublicCourseDetailAsync(
        [FromRoute] Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var result = await _publicCourseService.GetPublicCourseDetailAsync(
            GetOptionalCurrentUserId(),
            courseId,
            cancellationToken);

        return Ok(result);
    }

    private Guid? GetOptionalCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId)
            ? userId
            : null;
    }
}
