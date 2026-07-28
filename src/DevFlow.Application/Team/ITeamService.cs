using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;

namespace DevFlow.Application.Team;

public interface ITeamService
{
    /// <summary>Members of the current tenant (tenant-scoped via the ambient query filter), with User loaded.</summary>
    Task<IReadOnlyList<TenantMember>> GetMembersAsync(CancellationToken cancellationToken);

    /// <summary>Returns the created invitation and its one-time raw token — never persisted, so this is the only place it's ever available.</summary>
    Task<(Invitation Invitation, string RawToken)> InviteAsync(
        Guid tenantId,
        Guid invitedByUserId,
        InviteMemberInput input,
        CancellationToken cancellationToken);

    /// <summary>Creates or links the invited User, adds them to the invitation's tenant, and marks the invitation used.</summary>
    Task<(User User, TenantMember Membership)> AcceptInvitationAsync(AcceptInvitationInput input, CancellationToken cancellationToken);

    /// <summary>Null if memberId doesn't exist in the current tenant. Throws ForbiddenException if the change would leave the tenant with zero Owners.</summary>
    Task<TenantMember?> ChangeRoleAsync(Guid memberId, TenantRole newRole, CancellationToken cancellationToken);

    /// <summary>
    /// False if memberId doesn't exist in the current tenant. Throws
    /// ForbiddenException if a non-Owner is trying to remove an Owner, or if
    /// removal would leave the tenant with zero Owners.
    /// </summary>
    Task<bool> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken);
}
