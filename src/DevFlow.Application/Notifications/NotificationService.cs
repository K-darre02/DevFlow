using DevFlow.Application.Common;
using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Notifications;

public class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _context;

    public NotificationService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Notification>> GetNotificationsAsync(NotificationQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        // The query filter already restricts this to the current tenant
        // *and* user (DevFlowDbContext) — "users only see their own
        // notifications" isn't a Where clause here, it's structural.
        var notifications = _context.Notifications.AsNoTracking();

        var totalCount = await notifications.CountAsync(cancellationToken);

        // Ordering by CreatedAtTicks, not CreatedAt — see Notification.CreatedAtTicks / ActivityLogService for why.
        var items = await notifications
            .OrderByDescending(n => n.CreatedAtTicks)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Notification>(items, totalCount, page, pageSize);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        return await _context.Notifications.AsNoTracking().CountAsync(n => !n.IsRead, cancellationToken);
    }

    public async Task<Notification?> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

        if (notification is null)
        {
            return null;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return notification;
    }

    public async Task<int> MarkAllAsReadAsync(CancellationToken cancellationToken)
    {
        var unread = await _context.Notifications.Where(n => !n.IsRead).ToListAsync(cancellationToken);

        foreach (var notification in unread)
        {
            notification.IsRead = true;
        }

        if (unread.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return unread.Count;
    }
}
