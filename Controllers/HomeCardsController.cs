using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/home-cards")]
public sealed class HomeCardsController : ControllerBase
{
    private readonly IAdminService _adminService;

    public HomeCardsController(IAdminService adminService)
    {
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
    }

    [HttpGet]
    public async Task<IActionResult> GetHomeCardsAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetHomeCardsAsync(includeInactive: false, cancellationToken));
    }
}
