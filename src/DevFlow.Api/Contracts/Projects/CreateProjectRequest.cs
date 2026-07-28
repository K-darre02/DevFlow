using System.ComponentModel.DataAnnotations;

namespace DevFlow.Api.Contracts.Projects;

public record CreateProjectRequest
{
    [Required]
    public Guid TenantId { get; init; }

    [Required]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;
}
