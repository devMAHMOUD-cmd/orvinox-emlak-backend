using System.Security.Claims;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("general")]
[Route("api/seller/courses")]
public sealed class SellerCoursesController : ControllerBase
{
    private readonly ISellerCourseService _sellerCourseService;

    public SellerCoursesController(ISellerCourseService sellerCourseService)
    {
        _sellerCourseService = sellerCourseService ?? throw new ArgumentNullException(nameof(sellerCourseService));
    }

    [HttpGet]
    public async Task<IActionResult> GetSellerCoursesAsync(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.GetSellerCoursesAsync(
            GetCurrentUserId(),
            status,
            page,
            pageSize,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{courseId:guid}")]
    public async Task<IActionResult> GetSellerCourseDetailAsync(
        [FromRoute] Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.GetSellerCourseDetailAsync(
            GetCurrentUserId(),
            courseId,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSellerCourseAsync(
        [FromBody] CreateSellerCourseDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.CreateSellerCourseAsync(
            GetCurrentUserId(),
            dto,
            cancellationToken);

        return Ok(result);
    }

    [HttpPut("{courseId:guid}")]
    public async Task<IActionResult> UpdateSellerCourseAsync(
        [FromRoute] Guid courseId,
        [FromBody] UpdateSellerCourseDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.UpdateSellerCourseAsync(
            GetCurrentUserId(),
            courseId,
            dto,
            cancellationToken);

        return Ok(result);
    }

    [HttpPatch("{courseId:guid}/archive")]
    public async Task<IActionResult> ArchiveSellerCourseAsync(
        [FromRoute] Guid courseId,
        CancellationToken cancellationToken = default)
    {
        await _sellerCourseService.ArchiveSellerCourseAsync(
            GetCurrentUserId(),
            courseId,
            cancellationToken);

        return Ok(new { message = "Kurs arsivlendi." });
    }

    [HttpPost("{courseId:guid}/sections")]
    public async Task<IActionResult> CreateSectionAsync(
        [FromRoute] Guid courseId,
        [FromBody] CreateSellerCourseSectionDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.CreateSectionAsync(
            GetCurrentUserId(),
            courseId,
            dto,
            cancellationToken);

        return Ok(result);
    }

    [HttpPut("sections/{sectionId:guid}")]
    public async Task<IActionResult> UpdateSectionAsync(
        [FromRoute] Guid sectionId,
        [FromBody] UpdateSellerCourseSectionDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.UpdateSectionAsync(
            GetCurrentUserId(),
            sectionId,
            dto,
            cancellationToken);

        return Ok(result);
    }

    [HttpDelete("sections/{sectionId:guid}")]
    public async Task<IActionResult> DeleteSectionAsync(
        [FromRoute] Guid sectionId,
        CancellationToken cancellationToken = default)
    {
        await _sellerCourseService.DeleteSectionAsync(
            GetCurrentUserId(),
            sectionId,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("sections/{sectionId:guid}/lessons")]
    public async Task<IActionResult> CreateLessonAsync(
        [FromRoute] Guid sectionId,
        [FromBody] CreateSellerCourseLessonDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.CreateLessonAsync(
            GetCurrentUserId(),
            sectionId,
            dto,
            cancellationToken);

        return Ok(result);
    }

    [HttpPut("lessons/{lessonId:guid}")]
    public async Task<IActionResult> UpdateLessonAsync(
        [FromRoute] Guid lessonId,
        [FromBody] UpdateSellerCourseLessonDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCourseService.UpdateLessonAsync(
            GetCurrentUserId(),
            lessonId,
            dto,
            cancellationToken);

        return Ok(result);
    }

    [HttpDelete("lessons/{lessonId:guid}")]
    public async Task<IActionResult> DeleteLessonAsync(
        [FromRoute] Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        await _sellerCourseService.DeleteLessonAsync(
            GetCurrentUserId(),
            lessonId,
            cancellationToken);

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
