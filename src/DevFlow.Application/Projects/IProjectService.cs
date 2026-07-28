using DevFlow.Domain.Entities;

namespace DevFlow.Application.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<Project>> GetProjectsAsync(bool includeArchived, CancellationToken cancellationToken);

    Task<Project?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Project> CreateProjectAsync(Guid tenantId, string name, CancellationToken cancellationToken);

    Task<Project?> UpdateProjectAsync(Guid id, string? name, bool? isArchived, CancellationToken cancellationToken);

    Task<bool> DeleteProjectAsync(Guid id, CancellationToken cancellationToken);
}
