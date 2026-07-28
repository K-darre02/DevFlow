using System.ComponentModel.DataAnnotations;

namespace DevFlow.Api.Contracts.Auth;

public record RegisterRequest
{
    [Required]
    [MaxLength(200)]
    public string TenantName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;
}
