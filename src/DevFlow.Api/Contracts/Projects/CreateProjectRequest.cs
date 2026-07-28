using System.ComponentModel.DataAnnotations;

namespace DevFlow.Api.Contracts.Projects;

public record CreateProjectRequest
{
    // No TenantId here on purpose: it comes from the authenticated caller's
    // JWT (ICurrentUserService), never the client — accepting a client-supplied
    // TenantId would let any caller create data under an arbitrary tenant.
    [Required]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;
}
