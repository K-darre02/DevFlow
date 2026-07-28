using System.ComponentModel.DataAnnotations;

namespace DevFlow.Api.Contracts.Team;

public record AcceptInvitationRequest
{
    [Required]
    public string Token { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;
}
