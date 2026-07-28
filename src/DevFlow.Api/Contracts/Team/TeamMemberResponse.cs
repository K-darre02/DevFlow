using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Team;

public record TeamMemberResponse(
    Guid Id,
    Guid UserId,
    string Email,
    TenantRole Role,
    DateTimeOffset JoinedAt);
