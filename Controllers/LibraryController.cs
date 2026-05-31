using System.Security.Claims;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[Route("api/library")]
public sealed class LibraryController : ControllerBase
{
    private readonly ILibraryService _libraryService;

    public LibraryController(ILibraryService libraryService)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
    }

    [HttpGet]
    public async Task<IActionResult> GetMyLibraryAsync()
    {
        var userId = GetCurrentUserId();
        var result = await _libraryService.GetMyLibraryAsync(userId);

        return Ok(result);
    }

    [HttpPut("{productId:guid}/access")]
    public async Task<IActionResult> MarkAsAccessedAsync([FromRoute] Guid productId)
    {
        var userId = GetCurrentUserId();
        await _libraryService.MarkAsAccessedAsync(userId, productId);

        return Ok(new { message = "Kutuphane erisimi guncellendi." });
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
