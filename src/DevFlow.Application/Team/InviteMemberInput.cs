using DevFlow.Domain.Enums;

namespace DevFlow.Application.Team;

public record InviteMemberInput(string Email, TenantRole Role);
