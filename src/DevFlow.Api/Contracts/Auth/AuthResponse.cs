using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Auth;

public record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid TenantId,
    Guid UserId,
    string Email,
    TenantRole Role);
