using DevFlow.Application.Common;
using DevFlow.Application.Common.Exceptions;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Team;

public class TeamService : ITeamService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IValidator<InviteMemberInput> _inviteValidator;
    private readonly IValidator<AcceptInvitationInput> _acceptValidator;

    public TeamService(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IPasswordHasher<User> passwordHasher,
        IValidator<InviteMemberInput> inviteValidator,
        IValidator<AcceptInvitationInput> acceptValidator)
    {
        _context = context;
        _currentUserService = currentUserService;
        _passwordHasher = passwordHasher;
        _inviteValidator = inviteValidator;
        _acceptValidator = acceptValidator;
    }

    public async Task<IReadOnlyList<TenantMember>> GetMembersAsync(CancellationToken cancellationToken)
    {
        // Ordered client-side: SQLite's EF Core provider (used by this
        // solution's tests) can't translate ORDER BY on DateTimeOffset
        // columns, only Npgsql can. A tenant's member list is small enough
        // that sorting after materializing is not a meaningful cost.
        var members = await _context.TenantMembers
            .AsNoTracking()
            .Include(m => m.User)
            .ToListAsync(cancellationToken);

        return members.OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<(Invitation Invitation, string RawToken)> InviteAsync(
        Guid tenantId,
        Guid invitedByUserId,
        InviteMemberInput input,
        CancellationToken cancellationToken)
    {
        await _inviteValidator.ValidateAndThrowAsync(input, cancellationToken);

        var rawToken = InvitationTokens.Generate();

        var invitation = new Invitation
        {
            TenantId = tenantId,
            Email = input.Email,
            Role = input.Role,
            TokenHash = InvitationTokens.Hash(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            InvitedByUserId = invitedByUserId
        };

        _context.Invitations.Add(invitation);
        await _context.SaveChangesAsync(cancellationToken);

        return (invitation, rawToken);
    }

    public async Task<(User User, TenantMember Membership)> AcceptInvitationAsync(
        AcceptInvitationInput input,
        CancellationToken cancellationToken)
    {
        await _acceptValidator.ValidateAndThrowAsync(input, cancellationToken);

        // The validator already confirmed a matching, unexpired, unused
        // invitation exists — safe to load it directly here.
        var tokenHash = InvitationTokens.Hash(input.Token);
        var invitation = await _context.Invitations
            .IgnoreQueryFilters()
            .FirstAsync(i => i.TokenHash == tokenHash, cancellationToken);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == invitation.Email, cancellationToken);

        if (user is null)
        {
            user = new User { Email = invitation.Email };
            user.PasswordHash = _passwordHasher.HashPassword(user, input.Password);
            _context.Users.Add(user);
        }
        else
        {
            // Linking to an existing account: proves ownership the same way
            // Login does, rather than letting anyone who intercepts an
            // invitation link silently take over an existing account.
            var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, input.Password);

            if (verification == PasswordVerificationResult.Failed)
            {
                throw new ForbiddenException("Incorrect password for the existing account with this email.");
            }

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, input.Password);
            }
        }

        var membership = new TenantMember
        {
            TenantId = invitation.TenantId,
            UserId = user.Id,
            User = user,
            Role = invitation.Role
        };

        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        _context.TenantMembers.Add(membership);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Rare race: two still-valid invitations to the same tenant+email,
            // the first already accepted in a concurrent request — the unique
            // (TenantId, UserId) index on TenantMember catches it.
            throw new ForbiddenException("You are already a member of this workspace.");
        }

        return (user, membership);
    }

    public async Task<TenantMember?> ChangeRoleAsync(Guid memberId, TenantRole newRole, CancellationToken cancellationToken)
    {
        var member = await _context.TenantMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);

        if (member is null)
        {
            return null;
        }

        if (member.Role == TenantRole.Owner && newRole != TenantRole.Owner)
        {
            await EnsureAnotherOwnerExistsAsync(excludingMemberId: member.Id, cancellationToken);
        }

        member.Role = newRole;
        await _context.SaveChangesAsync(cancellationToken);

        return member;
    }

    public async Task<bool> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await _context.TenantMembers.FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);

        if (member is null)
        {
            return false;
        }

        if (member.Role == TenantRole.Owner)
        {
            // Policy-level authorization (AdminOrOwner on the DELETE endpoint)
            // lets an Admin call this at all — this is the domain-level rule
            // a static policy can't express: which specific member they're
            // allowed to remove.
            if (_currentUserService.Role != TenantRole.Owner)
            {
                throw new ForbiddenException("Only an Owner can remove another Owner.");
            }

            await EnsureAnotherOwnerExistsAsync(excludingMemberId: member.Id, cancellationToken);
        }

        _context.TenantMembers.Remove(member);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // Relies entirely on TenantMembers' ambient tenant query filter for
    // scoping — no explicit TenantId here, consistent with every other
    // service in this codebase (see ProjectService/TaskService).
    private async Task EnsureAnotherOwnerExistsAsync(Guid excludingMemberId, CancellationToken cancellationToken)
    {
        var remainingOwners = await _context.TenantMembers
            .CountAsync(m => m.Role == TenantRole.Owner && m.Id != excludingMemberId, cancellationToken);

        if (remainingOwners == 0)
        {
            throw new ForbiddenException("A workspace must always have at least one Owner.");
        }
    }
}
