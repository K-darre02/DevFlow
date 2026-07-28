using DevFlow.Api.Contracts.Projects;
using DevFlow.Application.Common;
using DevFlow.Domain.Entities;
using DevFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly DevFlowDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ProjectsController(DevFlowDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    // No explicit TenantId filter here, and none needed: DevFlowDbContext's
    // global query filter already scopes every query on this DbSet to the
    // authenticated caller's TenantId claim (see DevFlowDbContext and
    // docs/devflow/04-security.md §2). [Authorize] guarantees a valid JWT —
    // and therefore a resolvable TenantId — got this far.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> GetProjects(CancellationToken cancellationToken)
    {
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectResponse(p.Id, p.TenantId, p.Name, p.IsArchived, p.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(projects);
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

        var project = new Project
        {
            TenantId = tenantId,
            Name = request.Name
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new ProjectResponse(project.Id, project.TenantId, project.Name, project.IsArchived, project.CreatedAt);

        return CreatedAtAction(nameof(GetProjects), response);
    }
}
