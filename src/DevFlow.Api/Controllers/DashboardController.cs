using DevFlow.Api.Contracts.Activity;
using DevFlow.Api.Contracts.Dashboard;
using DevFlow.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

// Any authenticated tenant member can view the dashboard — same
// read-is-not-role-gated precedent as ActivityController/TeamController.GetTeam.
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> GetDashboard(CancellationToken cancellationToken)
    {
        var summary = await _dashboardService.GetSummaryAsync(cancellationToken);
        return Ok(ToResponse(summary));
    }

    private static DashboardResponse ToResponse(DashboardSummary summary) => new(
        summary.ProjectCount,
        summary.TaskCount,
        summary.CompletedTaskCount,
        summary.OverdueTaskCount,
        summary.TasksByStatus.ToDictionary(x => x.Status, x => x.Count),
        summary.TasksByPriority.ToDictionary(x => x.Priority, x => x.Count),
        summary.RecentActivity.Select(ActivityResponseMapper.ToResponse).ToList(),
        summary.OverdueTasks.Select(t => new OverdueTaskResponse(
            t.Id, t.Title, t.ProjectId, t.Project.Name, t.DueDate!.Value, t.Priority)).ToList());
}
