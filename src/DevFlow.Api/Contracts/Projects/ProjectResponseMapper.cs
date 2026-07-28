using DevFlow.Domain.Entities;

namespace DevFlow.Api.Contracts.Projects;

// Shared by ProjectsController and the Realtime notification handlers — see
// TaskResponseMapper for the same rationale.
public static class ProjectResponseMapper
{
    public static ProjectResponse ToResponse(Project project) =>
        new(project.Id, project.TenantId, project.Name, project.IsArchived, project.CreatedAt);
}
