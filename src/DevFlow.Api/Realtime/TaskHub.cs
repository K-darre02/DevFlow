using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DevFlow.Api.Realtime;

// [Authorize] here is enforced the same way as on a controller: SignalR
// checks it during the connection handshake using the same JWT bearer
// scheme (see DependencyInjection.AddJwtAuthentication's OnMessageReceived,
// which pulls the token from ?access_token= since browsers can't set an
// Authorization header on a WebSocket upgrade request). An unauthenticated
// or invalid-token connection is rejected before OnConnectedAsync ever runs.
[Authorize]
public class TaskHub : Hub
{
    // One group per tenant — every task/project broadcastable event is
    // tenant-wide (not scoped further to a single project/board), so this is
    // the only grouping tenant isolation needs there. A connection is added
    // to its own tenant's group and nothing else, straight from the JWT's
    // tenant_id claim — never anything the client sends, so a client cannot
    // ask to join another tenant's group.
    public static string GroupName(Guid tenantId) => $"tenant:{tenantId}";

    // Notifications are per-*user*, not tenant-wide — a tenant-wide
    // broadcast would leak "you were assigned this task" to everyone in the
    // tenant, not just the assignee. Same non-negotiable-input rule as the
    // tenant group: derived only from the JWT's own sub claim.
    public static string UserGroupName(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tenantId, out var parsedTenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(parsedTenantId));
        }

        var userId = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (Guid.TryParse(userId, out var parsedUserId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(parsedUserId));
        }

        await base.OnConnectedAsync();
    }
}
