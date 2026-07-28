using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Team;

// Token is the raw (unhashed) invitation token — present here and only
// here, exactly once, since it's never persisted (Invitation stores only
// its hash). There is no email-delivery mechanism in this system yet (see
// docs/devflow/01-architecture.md's SendGrid entry, which was documented
// but never wired up) — the inviter is responsible for sharing the
// resulting accept link out of band for now. This is a real, deliberate
// scope boundary, not a stub: everything about the invitation workflow
// itself (generation, expiry, single-use, accept) is fully implemented.
public record InvitationResponse(
    Guid Id,
    string Email,
    TenantRole Role,
    DateTimeOffset ExpiresAt,
    string Token);
