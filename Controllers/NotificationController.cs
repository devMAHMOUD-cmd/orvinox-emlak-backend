using System.Security.Claims;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    [HttpGet]
    public async Task<IActionResult> GetNotificationsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetCurrentUserId();
        var result = await _notificationService.GetUserNotificationsAsync(userId, page, pageSize);

        return Ok(result);
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsReadAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _notificationService.MarkAsReadAsync(id, userId);

        return Ok(new { message = "Bildirim okundu olarak işaretlendi." });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsReadAsync()
    {
        var userId = GetCurrentUserId();
        await _notificationService.MarkAllAsReadAsync(userId);

        return Ok(new { message = "Tüm bildirimler okundu olarak işaretlendi." });
    }

    [HttpPost("/api/devices/token")]
    public async Task<IActionResult> SaveDeviceTokenAsync([FromBody] SaveDeviceTokenDto dto)
    {
        var userId = GetCurrentUserId();
        await _notificationService.SaveDeviceTokenAsync(userId, dto);

        return Ok(new { message = "Cihaz token bilgisi kaydedildi." });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Geçersiz kullanıcı token'ı.");
        }

        return userId;
    }
}
