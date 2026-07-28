namespace DevFlow.Api.Contracts.Projects;

public record ProjectResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    bool IsArchived,
    DateTimeOffset CreatedAt);
