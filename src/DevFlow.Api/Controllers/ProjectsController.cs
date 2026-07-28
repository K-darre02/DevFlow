using DevFlow.Api.Contracts.Projects;
using DevFlow.Application.Common;
using DevFlow.Application.Projects;
using DevFlow.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ICurrentUserService _currentUserService;

    public ProjectsController(IProjectService projectService, ICurrentUserService currentUserService)
    {
        _projectService = projectService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> GetProjects(
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        var projects = await _projectService.GetProjectsAsync(includeArchived, cancellationToken);
        return Ok(projects.Select(ToResponse));
    }

    // Cross-tenant requests land here too (any GUID is a valid route value),
    // and correctly come back 404 rather than 403 — the service's query is
    // tenant-filtered, so a project belonging to another tenant is
    // indistinguishable from one that doesn't exist. See
    // docs/devflow/04-security.md §2.
    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectResponse>> GetProject(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectService.GetProjectByIdAsync(id, cancellationToken);
        return project is null ? NotFound() : Ok(ToResponse(project));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectResponse>> CreateProject(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        // Guaranteed non-null: [Authorize] rejects the request before this runs
        // unless the JWT validated successfully, and every token this system
        // issues carries a tenant_id claim (JwtTokenService).
        var tenantId = _currentUserService.TenantId!.Value;

        var project = await _projectService.CreateProjectAsync(tenantId, request.Name, cancellationToken);

        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, ToResponse(project));
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<ProjectResponse>> UpdateProject(
        Guid id,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var project = await _projectService.UpdateProjectAsync(id, request.Name, request.IsArchived, cancellationToken);
        return project is null ? NotFound() : Ok(ToResponse(project));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _projectService.DeleteProjectAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static ProjectResponse ToResponse(Project project) =>
        new(project.Id, project.TenantId, project.Name, project.IsArchived, project.CreatedAt);
}
