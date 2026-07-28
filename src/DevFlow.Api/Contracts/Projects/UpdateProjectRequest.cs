using System.ComponentModel.DataAnnotations;

namespace DevFlow.Api.Contracts.Projects;

public record UpdateProjectRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }

    public bool? IsArchived { get; init; }
}
