using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/pulse-news")]
public sealed class PulseNewsController : ControllerBase
{
    private readonly IAdminService _adminService;

    public PulseNewsController(IAdminService adminService)
    {
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
    }

    [HttpGet]
    public async Task<IActionResult> GetPulseNewsAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetPulseNewsAsync(includeUnpublished: false, cancellationToken));
    }
}
