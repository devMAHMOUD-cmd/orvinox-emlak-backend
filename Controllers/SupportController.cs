using System.Security.Claims;
using CraftoraApi.DTOs.Support;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[Route("api/support/tickets")]
public sealed class SupportController : ControllerBase
{
    private readonly ISupportTicketService _supportTicketService;

    public SupportController(ISupportTicketService supportTicketService)
    {
        _supportTicketService = supportTicketService ?? throw new ArgumentNullException(nameof(supportTicketService));
    }

    [HttpPost]
    [EnableRateLimiting("support-ticket-create")]
    public async Task<IActionResult> CreateTicketAsync([FromBody] CreateTicketDto dto)
    {
        var result = await _supportTicketService.CreateTicketAsync(GetCurrentUserId(), dto);
        return Created($"/api/support/tickets/{result.Id}", result);
    }

    [HttpGet]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> GetMyTicketsAsync(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _supportTicketService.GetMyTicketsAsync(
            GetCurrentUserId(),
            status,
            page,
            pageSize);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> GetTicketDetailAsync([FromRoute] Guid id)
    {
        var result = await _supportTicketService.GetMyTicketDetailAsync(GetCurrentUserId(), id);
        return Ok(result);
    }

    [HttpPost("{id:guid}/messages")]
    [EnableRateLimiting("support-ticket-message")]
    public async Task<IActionResult> AddMessageAsync(
        [FromRoute] Guid id,
        [FromBody] AddMessageDto dto)
    {
        var result = await _supportTicketService.AddMessageAsync(GetCurrentUserId(), id, dto);
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
