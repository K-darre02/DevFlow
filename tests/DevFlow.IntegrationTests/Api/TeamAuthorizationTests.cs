using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DevFlow.Api.Contracts.Auth;
using DevFlow.Api.Contracts.Team;
using DevFlow.Api.Services;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DevFlow.IntegrationTests.Api;

// End-to-end through the real pipeline (same rationale as AuthorizationTests):
// [Authorize] policies, TeamController, TeamService, and DevFlowDbContext's
// tenant query filter together — not just TeamService in isolation
// (see TeamServiceTests for that). One factory instance per test method for
// database isolation, same as AuthorizationTests.
public class TeamAuthorizationTests : IDisposable
{
    // Must match DependencyInjection.AddJsonOptions (JsonStringEnumConverter)
    // so requests/responses round-trip TenantRole the same way the real API
    // and browser client do.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DevFlowWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // Deliberately a single shared DbContext for all seeding within a test:
    // handing an entity tracked by one DbContext to a second DbContext's
    // AddRange (e.g. a TenantMember whose .Tenant navigation points at a
    // Tenant tracked elsewhere) makes EF treat that referenced Tenant as new
    // and try to re-insert it, violating the primary key.
    private async Task<DevFlowDbContext> CreateContextAsync() => await _factory.CreateDbContextAsync();

    private async Task<(Tenant Tenant, User Owner, TenantMember OwnerMembership, User Member, TenantMember MemberMembership)> SeedTenantWithOwnerAndMemberAsync(DevFlowDbContext context)
    {
        var tenant = new Tenant { Name = "Tenant" };
        var owner = new User { Email = "owner@example.com", PasswordHash = "unused" };
        var ownerMembership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = owner.Id, User = owner, Role = TenantRole.Owner };
        var member = new User { Email = "member@example.com", PasswordHash = "unused" };
        var memberMembership = new TenantMember { TenantId = tenant.Id, Tenant = tenant, UserId = member.Id, User = member, Role = TenantRole.Member };

        context.AddRange(tenant, owner, ownerMembership, member, memberMembership);
        await context.SaveChangesAsync();

        return (tenant, owner, ownerMembership, member, memberMembership);
    }

    private async Task<(User Admin, TenantMember Membership)> AddAdminAsync(DevFlowDbContext context, Tenant tenant)
    {
        var admin = new User { Email = "admin@example.com", PasswordHash = "unused" };
        var membership = new TenantMember { TenantId = tenant.Id, UserId = admin.Id, Role = TenantRole.Admin };
        context.AddRange(admin, membership);
        await context.SaveChangesAsync();
        return (admin, membership);
    }

    private static string GenerateToken(Guid tenantId, Guid userId, TenantRole role)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = DevFlowWebApplicationFactory.JwtIssuer,
                ["Jwt:Audience"] = DevFlowWebApplicationFactory.JwtAudience,
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:SigningKey"] = DevFlowWebApplicationFactory.JwtSigningKey
            })
            .Build();

        var user = new User { Id = userId, Email = "test@example.com" };
        var (accessToken, _) = new JwtTokenService(configuration).GenerateToken(user, tenantId, role);
        return accessToken;
    }

    private HttpClient AuthenticatedClient(Guid tenantId, Guid userId, TenantRole role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(tenantId, userId, role));
        return client;
    }

    [Fact]
    public async Task GetTeam_without_a_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/team");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTeam_only_returns_the_callers_tenant_members()
    {
        var context = await CreateContextAsync();
        var (tenantA, ownerA, _, _, _) = await SeedTenantWithOwnerAndMemberAsync(context);
        var tenantB = new Tenant { Name = "Tenant B" };
        var userB = new User { Email = "b@example.com", PasswordHash = "unused" };
        var membershipB = new TenantMember { TenantId = tenantB.Id, UserId = userB.Id, Role = TenantRole.Owner };
        context.AddRange(tenantB, userB, membershipB);
        await context.SaveChangesAsync();

        var client = AuthenticatedClient(tenantA.Id, ownerA.Id, TenantRole.Owner);
        var response = await client.GetAsync("/api/team");
        var members = await response.Content.ReadFromJsonAsync<List<TeamMemberResponse>>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        members.Should().HaveCount(2); // owner + member seeded for tenant A
        members.Should().NotContain(m => m.UserId == userB.Id);
    }

    [Fact]
    public async Task Invite_as_a_member_returns_403()
    {
        var context = await CreateContextAsync();
        var (tenant, _, _, member, _) = await SeedTenantWithOwnerAndMemberAsync(context);

        var client = AuthenticatedClient(tenant.Id, member.Id, TenantRole.Member);
        var response = await client.PostAsJsonAsync("/api/team/invitations", new { email = "new@example.com", role = TenantRole.Member }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invite_as_an_admin_succeeds_and_returns_the_raw_token()
    {
        var context = await CreateContextAsync();
        var (tenant, _, _, _, _) = await SeedTenantWithOwnerAndMemberAsync(context);
        var (admin, _) = await AddAdminAsync(context, tenant);

        var client = AuthenticatedClient(tenant.Id, admin.Id, TenantRole.Admin);
        var response = await client.PostAsJsonAsync("/api/team/invitations", new { email = "new@example.com", role = TenantRole.Member }, JsonOptions);
        var invitation = await response.Content.ReadFromJsonAsync<InvitationResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        invitation!.Email.Should().Be("new@example.com");
        invitation.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Invite_a_duplicate_pending_email_returns_400_with_problem_details()
    {
        var context = await CreateContextAsync();
        var (tenant, owner, _, _, _) = await SeedTenantWithOwnerAndMemberAsync(context);
        var client = AuthenticatedClient(tenant.Id, owner.Id, TenantRole.Owner);
        await client.PostAsJsonAsync("/api/team/invitations", new { email = "dup@example.com", role = TenantRole.Member }, JsonOptions);

        var response = await client.PostAsJsonAsync("/api/team/invitations", new { email = "dup@example.com", role = TenantRole.Member }, JsonOptions);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem!["errors"]!["Email"].Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptInvitation_with_a_valid_token_creates_an_account_and_returns_a_scoped_token()
    {
        var context = await CreateContextAsync();
        var (tenant, owner, _, _, _) = await SeedTenantWithOwnerAndMemberAsync(context);
        var ownerClient = AuthenticatedClient(tenant.Id, owner.Id, TenantRole.Owner);
        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/team/invitations", new { email = "invitee@example.com", role = TenantRole.Admin }, JsonOptions);
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<InvitationResponse>(JsonOptions);

        var anonymousClient = _factory.CreateClient();
        var acceptResponse = await anonymousClient.PostAsJsonAsync(
            "/api/team/invitations/accept",
            new { token = invitation!.Token, password = "a-secure-password" },
            JsonOptions);
        var auth = await acceptResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        auth!.TenantId.Should().Be(tenant.Id);
        auth.Role.Should().Be(TenantRole.Admin);
        auth.Email.Should().Be("invitee@example.com");
    }

    [Fact]
    public async Task AcceptInvitation_with_an_already_used_token_returns_400()
    {
        var context = await CreateContextAsync();
        var (tenant, owner, _, _, _) = await SeedTenantWithOwnerAndMemberAsync(context);
        var ownerClient = AuthenticatedClient(tenant.Id, owner.Id, TenantRole.Owner);
        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/team/invitations", new { email = "invitee@example.com", role = TenantRole.Member }, JsonOptions);
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<InvitationResponse>(JsonOptions);

        var anonymousClient = _factory.CreateClient();
        await anonymousClient.PostAsJsonAsync("/api/team/invitations/accept", new { token = invitation!.Token, password = "a-secure-password" }, JsonOptions);

        var secondAttempt = await anonymousClient.PostAsJsonAsync("/api/team/invitations/accept", new { token = invitation.Token, password = "a-secure-password" }, JsonOptions);

        secondAttempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AcceptInvitation_with_a_garbage_token_returns_400()
    {
        await CreateContextAsync(); // ensures the schema exists — this test seeds no rows itself
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/team/invitations/accept", new { token = "not-a-real-token", password = "a-secure-password" }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeRole_as_a_member_returns_403()
    {
        var context = await CreateContextAsync();
        var (tenant, _, _, member, memberMembership) = await SeedTenantWithOwnerAndMemberAsync(context);

        var client = AuthenticatedClient(tenant.Id, member.Id, TenantRole.Member);
        var response = await client.PatchAsJsonAsync($"/api/team/{memberMembership.Id}/role", new { role = TenantRole.Admin }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeRole_as_an_admin_returns_403_because_only_owners_may_change_roles()
    {
        var context = await CreateContextAsync();
        var (tenant, _, _, _, memberMembership) = await SeedTenantWithOwnerAndMemberAsync(context);
        var (admin, _) = await AddAdminAsync(context, tenant);

        var client = AuthenticatedClient(tenant.Id, admin.Id, TenantRole.Admin);
        var response = await client.PatchAsJsonAsync($"/api/team/{memberMembership.Id}/role", new { role = TenantRole.Admin }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangeRole_as_an_owner_succeeds()
    {
        var context = await CreateContextAsync();
        var (tenant, owner, _, _, memberMembership) = await SeedTenantWithOwnerAndMemberAsync(context);

        var client = AuthenticatedClient(tenant.Id, owner.Id, TenantRole.Owner);
        var response = await client.PatchAsJsonAsync($"/api/team/{memberMembership.Id}/role", new { role = TenantRole.Admin }, JsonOptions);
        var updated = await response.Content.ReadFromJsonAsync<TeamMemberResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Role.Should().Be(TenantRole.Admin);
    }

    [Fact]
    public async Task ChangeRole_for_a_member_id_in_another_tenant_returns_404_not_403()
    {
        var context = await CreateContextAsync();
        var (tenantA, ownerA, _, _, _) = await SeedTenantWithOwnerAndMemberAsync(context);
        var tenantB = new Tenant { Name = "Tenant B" };
        var userB = new User { Email = "b@example.com", PasswordHash = "unused" };
        var membershipB = new TenantMember { TenantId = tenantB.Id, UserId = userB.Id, Role = TenantRole.Member };
        context.AddRange(tenantB, userB, membershipB);
        await context.SaveChangesAsync();

        var client = AuthenticatedClient(tenantA.Id, ownerA.Id, TenantRole.Owner);
        var response = await client.PatchAsJsonAsync($"/api/team/{membershipB.Id}/role", new { role = TenantRole.Admin }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveMember_as_a_member_returns_403()
    {
        var context = await CreateContextAsync();
        var (tenant, _, ownerMembership, member, _) = await SeedTenantWithOwnerAndMemberAsync(context);

        var client = AuthenticatedClient(tenant.Id, member.Id, TenantRole.Member);
        var response = await client.DeleteAsync($"/api/team/{ownerMembership.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveMember_as_an_admin_succeeds_for_a_non_owner()
    {
        var context = await CreateContextAsync();
        var (tenant, _, _, _, memberMembership) = await SeedTenantWithOwnerAndMemberAsync(context);
        var (admin, _) = await AddAdminAsync(context, tenant);

        var client = AuthenticatedClient(tenant.Id, admin.Id, TenantRole.Admin);
        var response = await client.DeleteAsync($"/api/team/{memberMembership.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveMember_as_an_admin_trying_to_remove_the_owner_returns_403()
    {
        var context = await CreateContextAsync();
        var (tenant, _, ownerMembership, _, _) = await SeedTenantWithOwnerAndMemberAsync(context);
        var (admin, _) = await AddAdminAsync(context, tenant);

        var client = AuthenticatedClient(tenant.Id, admin.Id, TenantRole.Admin);
        var response = await client.DeleteAsync($"/api/team/{ownerMembership.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
