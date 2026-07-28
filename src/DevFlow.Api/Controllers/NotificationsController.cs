using DevFlow.Api.Contracts.Common;
using DevFlow.Api.Contracts.Notifications;
using DevFlow.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

// "Users only see their own notifications" isn't enforced here — it's
// enforced structurally by DevFlowDbContext's query filter on Notification
// (TenantId *and* UserId), the same way tenant isolation itself is. Every
// method below just calls into INotificationService and lets that filter do
// the actual security work; there's no separate ownership check to forget.
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<NotificationResponse>>> GetNotifications(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var query = new NotificationQuery(page ?? 1, pageSize ?? 20);
        var result = await _notificationService.GetNotificationsAsync(query, cancellationToken);

        return Ok(new PagedResponse<NotificationResponse>(
            result.Items.Select(NotificationResponseMapper.ToResponse).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount(CancellationToken cancellationToken)
    {
        var count = await _notificationService.GetUnreadCountAsync(cancellationToken);
        return Ok(new UnreadCountResponse(count));
    }

    [HttpPatch("{id}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var notification = await _notificationService.MarkAsReadAsync(id, cancellationToken);
        return notification is null ? NotFound() : Ok(NotificationResponseMapper.ToResponse(notification));
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        await _notificationService.MarkAllAsReadAsync(cancellationToken);
        return NoContent();
    }
}
