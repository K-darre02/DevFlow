using DevFlow.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Activities;

public class ActivityLogService : IActivityLogService
{
    private readonly IApplicationDbContext _context;

    public ActivityLogService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<DevFlow.Domain.Entities.ActivityLog>> GetActivitiesAsync(
        ActivityLogQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var filtered = _context.ActivityLogs.AsNoTracking().Include(a => a.User).AsQueryable();

        if (query.EntityType is not null)
        {
            filtered = filtered.Where(a => a.EntityType == query.EntityType);
        }

        if (query.UserId is not null)
        {
            filtered = filtered.Where(a => a.UserId == query.UserId);
        }

        if (query.ActivityType is not null)
        {
            filtered = filtered.Where(a => a.ActivityType == query.ActivityType);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        // Ordering by CreatedAtTicks (a plain long), not CreatedAt itself —
        // see the property's doc comment on ActivityLog for why: SQLite's
        // EF Core provider can't translate ORDER BY on a DateTimeOffset
        // column, or even DateTimeOffset.Ticks computed inline, only a
        // genuinely persisted plain-integer column. Pagination genuinely
        // needs this pushed to SQL — Skip/Take without a server-side
        // ORDER BY isn't reliably deterministic across pages, so unlike
        // TeamService.GetMembersAsync, sorting a materialized list in
        // memory isn't an option here.
        var items = await filtered
            .OrderByDescending(a => a.CreatedAtTicks)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DevFlow.Domain.Entities.ActivityLog>(items, totalCount, page, pageSize);
    }
}
