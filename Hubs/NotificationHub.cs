using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CraftoraApi.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }
}
