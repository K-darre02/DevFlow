using DevFlow.Api.Contracts.Activity;
using DevFlow.Api.Contracts.Common;
using DevFlow.Application.Activities;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

// Any authenticated tenant member can view the activity log — same
// read-is-not-role-gated precedent as TeamController.GetTeam. Nothing here
// is sensitive to expose within a tenant; role gating only applies to
// mutating team actions.
[ApiController]
[Route("api/activity")]
[Authorize]
public class ActivityController : ControllerBase
{
    private readonly IActivityLogService _activityLogService;

    public ActivityController(IActivityLogService activityLogService)
    {
        _activityLogService = activityLogService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ActivityResponse>>> GetActivities(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] ActivityEntityType? entityType,
        [FromQuery] Guid? userId,
        [FromQuery] ActivityType? activityType,
        CancellationToken cancellationToken)
    {
        // ActivityLogService.GetActivitiesAsync additionally clamps Page to
        // at least 1 and PageSize into [1, 100] regardless of what's passed.
        var query = new ActivityLogQuery(page ?? 1, pageSize ?? 20, entityType, userId, activityType);

        var result = await _activityLogService.GetActivitiesAsync(query, cancellationToken);

        return Ok(new PagedResponse<ActivityResponse>(
            result.Items.Select(ToResponse).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize));
    }

    private static ActivityResponse ToResponse(ActivityLog activity) => new(
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
