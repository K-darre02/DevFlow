using DevFlow.Api.Contracts.Projects;
using DevFlow.Domain.Entities;
using DevFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly DevFlowDbContext _dbContext;

    public ProjectsController(DevFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Tenant scoping is a required query parameter for now rather than derived
    // from a JWT claim, because auth/tenant-resolution middleware doesn't exist
    // yet (see docs/devflow/04-security.md §2 for the target design). This is a
    // deliberate, temporary simplification for this first vertical slice — not
    // a claim that this endpoint is tenant-isolation-safe as-is.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> GetProjects(
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest("tenantId is required.");
        }

        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsArchived)
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
        var tenantExists = await _dbContext.Tenants
            .AnyAsync(t => t.Id == request.TenantId, cancellationToken);

        if (!tenantExists)
        {
            return BadRequest($"Tenant '{request.TenantId}' does not exist.");
        }

        var project = new Project
        {
            TenantId = request.TenantId,
            Name = request.Name
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new ProjectResponse(project.Id, project.TenantId, project.Name, project.IsArchived, project.CreatedAt);

        return CreatedAtAction(nameof(GetProjects), new { tenantId = project.TenantId }, response);
    }
}
