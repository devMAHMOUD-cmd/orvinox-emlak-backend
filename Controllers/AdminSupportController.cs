using System.Security.Claims;
using CraftoraApi.DTOs.Support;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("general")]
[Route("api/admin/support/tickets")]
public sealed class AdminSupportController : ControllerBase
{
    private readonly ISupportTicketService _supportTicketService;

    public AdminSupportController(ISupportTicketService supportTicketService)
    {
        _supportTicketService = supportTicketService ?? throw new ArgumentNullException(nameof(supportTicketService));
    }

    [HttpGet]
    public async Task<IActionResult> GetTicketsAsync(
        [FromQuery] string? status,
        [FromQuery] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _supportTicketService.GetAllTicketsAsync(status, query, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTicketDetailAsync([FromRoute] Guid id)
    {
        var result = await _supportTicketService.GetTicketDetailAsync(id);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reply")]
    [EnableRateLimiting("support-ticket-message")]
    public async Task<IActionResult> AddReplyAsync(
        [FromRoute] Guid id,
        [FromBody] AdminReplyDto dto)
    {
        var result = await _supportTicketService.AddAdminReplyAsync(GetCurrentUserId(), id, dto);
        return Ok(result);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatusAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateTicketStatusDto dto)
    {
        var result = await _supportTicketService.UpdateStatusAsync(GetCurrentUserId(), id, dto);
        return Ok(result);
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
