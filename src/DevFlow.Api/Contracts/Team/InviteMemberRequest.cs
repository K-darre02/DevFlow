using System.ComponentModel.DataAnnotations;
using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Team;

public record InviteMemberRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public TenantRole Role { get; init; } = TenantRole.Member;
}
