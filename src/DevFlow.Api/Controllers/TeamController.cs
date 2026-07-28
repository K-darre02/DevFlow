using DevFlow.Api.Contracts.Team;
using DevFlow.Api.Services;
using DevFlow.Application.Common;
using DevFlow.Application.Team;
using DevFlow.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

[ApiController]
[Route("api/team")]
[Authorize]
public class TeamController : ControllerBase
{
    private readonly ITeamService _teamService;
    private readonly ICurrentUserService _currentUserService;
    private readonly JwtTokenService _tokenService;

    public TeamController(ITeamService teamService, ICurrentUserService currentUserService, JwtTokenService tokenService)
    {
        _teamService = teamService;
        _currentUserService = currentUserService;
        _tokenService = tokenService;
    }

    // Any authenticated member can view the roster — Members get a
    // read-only page on the frontend, but that's a UI concern, not an API
    // one: nothing here is sensitive to expose to any tenant member.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamMemberResponse>>> GetTeam(CancellationToken cancellationToken)
    {
        var members = await _teamService.GetMembersAsync(cancellationToken);
        return Ok(members.Select(ToResponse));
    }

    [HttpPost("invitations")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrOwner)]
    public async Task<ActionResult<InvitationResponse>> Invite(
        [FromBody] InviteMemberRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.TenantId!.Value;
        var invitedByUserId = _currentUserService.UserId!.Value;

        var (invitation, rawToken) = await _teamService.InviteAsync(
            tenantId,
            invitedByUserId,
            new InviteMemberInput(request.Email, request.Role),
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new InvitationResponse(invitation.Id, invitation.Email, invitation.Role, invitation.ExpiresAt, rawToken));
    }

    // Anonymous: accepting an invitation is how a brand-new user gets their
    // first session — there's nothing to authenticate yet. Issues a JWT the
    // same way Register/Login do, for the tenant+role the invitation grants.
    [HttpPost("invitations/accept")]
    [AllowAnonymous]
    public async Task<ActionResult<Contracts.Auth.AuthResponse>> AcceptInvitation(
        [FromBody] AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var (user, membership) = await _teamService.AcceptInvitationAsync(
            new AcceptInvitationInput(request.Token, request.Password),
            cancellationToken);

        var (accessToken, expiresAt) = _tokenService.GenerateToken(user, membership.TenantId, membership.Role);

        return Ok(new Contracts.Auth.AuthResponse(accessToken, expiresAt, membership.TenantId, user.Id, user.Email, membership.Role));
    }

    // Owner-only: Admin's explicit permissions (invite, remove) don't
    // include role management — see docs/devflow/04-security.md §3's
    // "Owner > Admin > Member" hierarchy and this feature's own
    // requirements. The zero-Owner-remaining invariant is enforced in
    // TeamService, not here — this endpoint only gates *who* may attempt it.
    [HttpPatch("{memberId}/role")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    public async Task<ActionResult<TeamMemberResponse>> ChangeRole(
        Guid memberId,
        [FromBody] ChangeRoleRequest request,
        CancellationToken cancellationToken)
    {
        var member = await _teamService.ChangeRoleAsync(memberId, request.Role, cancellationToken);
        return member is null ? NotFound() : Ok(ToResponse(member));
    }

    [HttpDelete("{memberId}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrOwner)]
    public async Task<IActionResult> RemoveMember(Guid memberId, CancellationToken cancellationToken)
    {
        var removed = await _teamService.RemoveMemberAsync(memberId, cancellationToken);
        return removed ? NoContent() : NotFound();
    }

    private static TeamMemberResponse ToResponse(TenantMember member) =>
        new(member.Id, member.UserId, member.User.Email, member.Role, member.CreatedAt);
}
