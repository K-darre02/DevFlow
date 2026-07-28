using DevFlow.Domain.Entities;

namespace DevFlow.Api.Contracts.Activity;

// Shared by ActivityController and DashboardController (recent activity
// widget) — same rationale as TaskResponseMapper.
public static class ActivityResponseMapper
{
    public static ActivityResponse ToResponse(ActivityLog activity) => new(
        activity.Id,
        activity.ActivityType,
        activity.EntityType,
        activity.EntityId,
        activity.Description,
        activity.Metadata,
        activity.UserId,
        activity.User?.Email,
        activity.CreatedAt);
}
