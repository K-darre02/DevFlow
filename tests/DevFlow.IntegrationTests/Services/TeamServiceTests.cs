using DevFlow.Application.Common.Exceptions;
using DevFlow.Application.Team;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevFlow.IntegrationTests.Services;

// Real validators (DB-aware, same as production) rather than mocks — the
// duplicate/existing-member/expired/reused checks they perform are exactly
// what's under test here alongside TeamService itself. Real PasswordHasher
// too: AcceptInvitationAsync's create-or-link logic depends on its actual
// hash/verify behavior, not just that some hasher was called.
public class TeamServiceTests : SqliteContextFixture
{
    private readonly ITeamService _service;

    public TeamServiceTests()
    {
        _service = new TeamService(
            DbContext,
            CurrentUser,
            new PasswordHasher<User>(),
            new InviteMemberInputValidator(DbContext),
            new AcceptInvitationInputValidator(DbContext));
    }

    private async Task<(Tenant Tenant, User Owner, TenantMember Membership)> SeedTenantWithOwnerAsync()
    {
        var tenant = new Tenant { Name = "Tenant" };
        var owner = new User { Email = "owner@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = owner.Id, User = owner, Role = TenantRole.Owner };

        DbContext.AddRange(tenant, owner, membership);
        await DbContext.SaveChangesAsync();

        CurrentUser.TenantId = tenant.Id;
        CurrentUser.UserId = owner.Id;
        CurrentUser.Role = TenantRole.Owner;

        return (tenant, owner, membership);
    }

    [Fact]
    public async Task GetMembersAsync_returns_only_current_tenant_members_ordered_by_joined_date()
    {
        var (tenantA, ownerA, _) = await SeedTenantWithOwnerAsync();
        var tenantB = new Tenant { Name = "Tenant B" };
        var userB = new User { Email = "b@example.com", PasswordHash = "unused" };
        var membershipB = new TenantMember { TenantId = tenantB.Id, Tenant = tenantB, UserId = userB.Id, User = userB, Role = TenantRole.Owner };
        DbContext.AddRange(tenantB, userB, membershipB);
        await DbContext.SaveChangesAsync();

        CurrentUser.TenantId = tenantA.Id;
        var members = await _service.GetMembersAsync(default);

        members.Should().ContainSingle().Which.UserId.Should().Be(ownerA.Id);
    }

    [Fact]
    public async Task InviteAsync_creates_an_invitation_expiring_in_seven_days()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();

        var (invitation, rawToken) = await _service.InviteAsync(
            tenant.Id, owner.Id, new InviteMemberInput("newperson@example.com", TenantRole.Member), default);

        invitation.Email.Should().Be("newperson@example.com");
        invitation.TokenHash.Should().NotBe(rawToken); // never stores the raw token
        invitation.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));
        invitation.AcceptedAt.Should().BeNull();
    }

    [Fact]
    public async Task InviteAsync_rejects_a_duplicate_pending_invitation_to_the_same_email()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();
        await _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("dup@example.com", TenantRole.Member), default);

        var act = () => _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("dup@example.com", TenantRole.Member), default);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already a pending invitation*");
    }

    [Fact]
    public async Task InviteAsync_rejects_an_email_that_already_belongs_to_a_member()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();

        var act = () => _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput(owner.Email, TenantRole.Member), default);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already a member*");
    }

    [Fact]
    public async Task InviteAsync_rejects_an_invalid_email_address()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();

        var act = () => _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("not-an-email", TenantRole.Member), default);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task InviteAsync_rejects_granting_the_owner_role()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();

        var act = () => _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("newperson@example.com", TenantRole.Owner), default);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Member or Admin*");
    }

    [Fact]
    public async Task AcceptInvitationAsync_creates_a_new_user_and_adds_them_to_the_tenant()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();
        var (invitation, rawToken) = await _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("newperson@example.com", TenantRole.Member), default);

        var (user, membership) = await _service.AcceptInvitationAsync(new AcceptInvitationInput(rawToken, "correct-password"), default);

        user.Email.Should().Be("newperson@example.com");
        membership.TenantId.Should().Be(tenant.Id);
        membership.Role.Should().Be(TenantRole.Member);

        var reloaded = await DbContext.Invitations.IgnoreQueryFilters().FirstAsync(i => i.Id == invitation.Id);
        reloaded.AcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptInvitationAsync_links_an_existing_user_when_the_password_matches()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();

        var hasher = new PasswordHasher<User>();
        var existingUser = new User { Email = "existing@example.com" };
        existingUser.PasswordHash = hasher.HashPassword(existingUser, "their-existing-password");
        DbContext.Users.Add(existingUser);
        await DbContext.SaveChangesAsync();

        var (_, rawToken) = await _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("existing@example.com", TenantRole.Admin), default);

        var (user, membership) = await _service.AcceptInvitationAsync(new AcceptInvitationInput(rawToken, "their-existing-password"), default);

        user.Id.Should().Be(existingUser.Id);
        membership.Role.Should().Be(TenantRole.Admin);
    }

    [Fact]
    public async Task AcceptInvitationAsync_throws_forbidden_when_existing_users_password_does_not_match()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();

        var hasher = new PasswordHasher<User>();
        var existingUser = new User { Email = "existing@example.com" };
        existingUser.PasswordHash = hasher.HashPassword(existingUser, "their-existing-password");
        DbContext.Users.Add(existingUser);
        await DbContext.SaveChangesAsync();

        var (_, rawToken) = await _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("existing@example.com", TenantRole.Admin), default);

        var act = () => _service.AcceptInvitationAsync(new AcceptInvitationInput(rawToken, "wrong-password"), default);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*Incorrect password*");
    }

    [Fact]
    public async Task AcceptInvitationAsync_rejects_an_expired_invitation()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();
        var (invitation, rawToken) = await _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("newperson@example.com", TenantRole.Member), default);

        var tracked = await DbContext.Invitations.IgnoreQueryFilters().FirstAsync(i => i.Id == invitation.Id);
        tracked.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await DbContext.SaveChangesAsync();

        var act = () => _service.AcceptInvitationAsync(new AcceptInvitationInput(rawToken, "correct-password"), default);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*expired*");
    }

    [Fact]
    public async Task AcceptInvitationAsync_rejects_a_token_that_was_already_used()
    {
        var (tenant, owner, _) = await SeedTenantWithOwnerAsync();
        var (_, rawToken) = await _service.InviteAsync(tenant.Id, owner.Id, new InviteMemberInput("newperson@example.com", TenantRole.Member), default);
        await _service.AcceptInvitationAsync(new AcceptInvitationInput(rawToken, "correct-password"), default);

        var act = () => _service.AcceptInvitationAsync(new AcceptInvitationInput(rawToken, "correct-password"), default);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*already been used*");
    }

    [Fact]
    public async Task ChangeRoleAsync_updates_the_members_role()
    {
        var (tenant, _, _) = await SeedTenantWithOwnerAsync();
        var member = new User { Email = "member@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = member.Id, User = member, Role = TenantRole.Member };
        DbContext.AddRange(member, membership);
        await DbContext.SaveChangesAsync();

        var updated = await _service.ChangeRoleAsync(membership.Id, TenantRole.Admin, default);

        updated.Should().NotBeNull();
        updated!.Role.Should().Be(TenantRole.Admin);
    }

    [Fact]
    public async Task ChangeRoleAsync_returns_null_for_a_member_that_does_not_exist()
    {
        await SeedTenantWithOwnerAsync();

        var result = await _service.ChangeRoleAsync(Guid.NewGuid(), TenantRole.Admin, default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ChangeRoleAsync_throws_forbidden_when_demoting_the_only_owner()
    {
        var (_, _, membership) = await SeedTenantWithOwnerAsync();

        var act = () => _service.ChangeRoleAsync(membership.Id, TenantRole.Admin, default);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*at least one Owner*");
    }

    [Fact]
    public async Task RemoveMemberAsync_removes_the_member()
    {
        var (tenant, _, _) = await SeedTenantWithOwnerAsync();
        var member = new User { Email = "member@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = member.Id, User = member, Role = TenantRole.Member };
        DbContext.AddRange(member, membership);
        await DbContext.SaveChangesAsync();

        var removed = await _service.RemoveMemberAsync(membership.Id, default);

        removed.Should().BeTrue();
        (await DbContext.TenantMembers.AnyAsync(m => m.Id == membership.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveMemberAsync_returns_false_for_a_member_that_does_not_exist()
    {
        await SeedTenantWithOwnerAsync();

        var result = await _service.RemoveMemberAsync(Guid.NewGuid(), default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveMemberAsync_throws_forbidden_when_an_admin_tries_to_remove_an_owner()
    {
        var (tenant, _, ownerMembership) = await SeedTenantWithOwnerAsync();
        var secondOwnerUser = new User { Email = "owner2@example.com", PasswordHash = "unused" };
        var secondOwnerMembership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = secondOwnerUser.Id, User = secondOwnerUser, Role = TenantRole.Owner };
        DbContext.AddRange(secondOwnerUser, secondOwnerMembership);
        await DbContext.SaveChangesAsync();

        // Acting as an Admin, not an Owner, attempting to remove the first Owner.
        CurrentUser.Role = TenantRole.Admin;

        var act = () => _service.RemoveMemberAsync(ownerMembership.Id, default);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*Only an Owner can remove another Owner*");
    }

    [Fact]
    public async Task RemoveMemberAsync_throws_forbidden_when_removing_the_only_owner()
    {
        var (_, _, membership) = await SeedTenantWithOwnerAsync();

        var act = () => _service.RemoveMemberAsync(membership.Id, default);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*at least one Owner*");
    }
}
