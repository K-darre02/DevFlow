using DevFlow.Application.Common;
using DevFlow.Domain.Enums;

namespace DevFlow.Application.Activities;

// Shared by every ActivityLog notification handler (Task/Project/Team) so
// "add the row, save" isn't duplicated twelve times. Deliberately does not
// catch its own exceptions: a failure here should propagate up through
// MediatR's Publish call to the originating service's PublishSafeAsync,
// which is where the "don't fail an otherwise-successful write" decision
// belongs — this helper doesn't know whether it's running standalone or
// alongside sibling handlers (TaskWhenAllPublisher), so it isn't the right
// place to decide that.
internal static class ActivityLogWriter
{
    public static async Task WriteAsync(
        IApplicationDbContext context,
        Guid tenantId,
        Guid? userId,
        ActivityType activityType,
        ActivityEntityType entityType,
        Guid entityId,
        string description,
        string? metadata,
        CancellationToken cancellationToken)
    {
        var entry = new Domain.Entities.ActivityLog
        {
            TenantId = tenantId,
            UserId = userId,
            ActivityType = activityType,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            Metadata = metadata
        };

        context.ActivityLogs.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
    }
}
