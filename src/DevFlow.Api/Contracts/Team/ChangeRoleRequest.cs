using System.ComponentModel.DataAnnotations;
using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Team;

public record ChangeRoleRequest
{
    [Required]
    public TenantRole Role { get; init; }
}
