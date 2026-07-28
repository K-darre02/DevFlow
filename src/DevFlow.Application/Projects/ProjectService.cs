using DevFlow.Application.Common;
using DevFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Projects;

public class ProjectService : IProjectService
{
    private readonly IApplicationDbContext _context;

    public ProjectService(IApplicationDbContext context)
    {
        _context = context;
    }

    // No explicit TenantId filter anywhere in this class — every query below
    // is automatically scoped by DevFlowDbContext's global query filter.
    // That's deliberate: it's the same structural guarantee documented in
    // docs/devflow/04-security.md §2, not something re-implemented per method.
    public async Task<IReadOnlyList<Project>> GetProjectsAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = _context.Projects.AsNoTracking().AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task<Project?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Project> CreateProjectAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var project = new Project
        {
            TenantId = tenantId,
            Name = name
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken);

        return project;
    }

    public async Task<Project?> UpdateProjectAsync(Guid id, string? name, bool? isArchived, CancellationToken cancellationToken)
    {
        var project = await _context.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return null;
        }

        if (name is not null)
        {
            project.Name = name;
        }

        if (isArchived is not null)
        {
            project.IsArchived = isArchived.Value;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return project;
    }

    public async Task<bool> DeleteProjectAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await _context.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return false;
        }

        // Cascades to the project's TaskItems at the database level
        // (ProjectConfiguration / TaskItemConfiguration).
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
