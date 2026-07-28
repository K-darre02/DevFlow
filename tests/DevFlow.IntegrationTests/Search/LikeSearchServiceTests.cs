using DevFlow.Application.Search;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.IntegrationTests.TestSupport;
using FluentAssertions;
using Xunit;

namespace DevFlow.IntegrationTests.Search;

public class LikeSearchServiceTests : SqliteContextFixture
{
    private readonly ISearchService _service;
    private readonly Tenant _tenant;
    private readonly Project _project;

    public LikeSearchServiceTests()
    {
        _service = new LikeSearchService(DbContext);

        _tenant = new Tenant { Name = "Tenant" };
        _project = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Apollo Website" };
        DbContext.AddRange(_tenant, _project);
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();

        CurrentUser.TenantId = _tenant.Id;
    }

    private TaskItem NewTask(string title, string? description = null) => new()
    {
        TenantId = _tenant.Id,
        Tenant = _tenant,
        ProjectId = _project.Id,
        Project = _project,
        Title = title,
        Description = description
    };

    private (User User, TenantMember Member) NewMember(string email, TenantRole role = TenantRole.Member)
    {
        var user = new User { Email = email, PasswordHash = "unused" };
        var member = new TenantMember { TenantId = _tenant.Id, Tenant = _tenant, UserId = user.Id, User = user, Role = role };
        return (user, member);
    }

    [Fact]
    public async Task SearchAsync_matches_projects_by_name()
    {
        var results = await _service.SearchAsync("apollo", 1, 10, default);

        results.Projects.Items.Should().ContainSingle(p => p.Id == _project.Id);
        results.Projects.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_matches_tasks_by_title_or_description()
    {
        DbContext.AddRange(
            NewTask("Fix login bug"),
            NewTask("Update docs", description: "Mentions login flow"),
            NewTask("Unrelated task"));
        await DbContext.SaveChangesAsync();

        var results = await _service.SearchAsync("login", 1, 10, default);

        results.Tasks.Items.Select(t => t.Title).Should().BeEquivalentTo("Fix login bug", "Update docs");
        results.Tasks.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_matches_users_by_email_via_tenant_membership()
    {
        var (_, member) = NewMember("alice@example.com");
        DbContext.AddRange(member.User, member);
        await DbContext.SaveChangesAsync();

        var results = await _service.SearchAsync("alice", 1, 10, default);

        results.Users.Items.Should().ContainSingle(u => u.Email == "alice@example.com");
        results.Users.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_never_returns_a_user_who_is_not_a_member_of_the_current_tenant()
    {
        // A User row can exist globally (e.g. a member of some other
        // tenant) without a TenantMember in *this* tenant. Matching on
        // Users directly would leak it; going through TenantMembers must not.
        var outsider = new User { Email = "outsider-alice@example.com", PasswordHash = "unused" };
        DbContext.Users.Add(outsider);
        await DbContext.SaveChangesAsync();

        var results = await _service.SearchAsync("alice", 1, 10, default);

        results.Users.Items.Should().BeEmpty();
        results.Users.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_ranks_prefix_matches_above_mid_string_matches()
    {
        var midMatch = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Redeploy service" };
        var prefixMatch = new Project { TenantId = _tenant.Id, Tenant = _tenant, Name = "Deploy pipeline" };
        DbContext.AddRange(midMatch, prefixMatch);
        await DbContext.SaveChangesAsync();

        var results = await _service.SearchAsync("deploy", 1, 10, default);

        results.Projects.Items.Select(p => p.Name).Should().ContainInOrder("Deploy pipeline", "Redeploy service");
        results.Projects.Items.First(p => p.Name == "Deploy pipeline").Rank
            .Should().BeGreaterThan(results.Projects.Items.First(p => p.Name == "Redeploy service").Rank);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_groups_for_a_blank_query()
    {
        var results = await _service.SearchAsync("   ", 1, 10, default);

        results.Projects.Items.Should().BeEmpty();
        results.Tasks.Items.Should().BeEmpty();
        results.Users.Items.Should().BeEmpty();
        results.Projects.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_groups_when_nothing_matches()
    {
        var results = await _service.SearchAsync("no-such-term-xyz", 1, 10, default);

        results.Projects.Items.Should().BeEmpty();
        results.Tasks.Items.Should().BeEmpty();
        results.Users.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_paginates_within_a_group()
    {
        DbContext.AddRange(
            NewTask("Alpha task"),
            NewTask("Alpha bravo"),
            NewTask("Alpha charlie"));
        await DbContext.SaveChangesAsync();

        var firstPage = await _service.SearchAsync("alpha", 1, 2, default);
        var secondPage = await _service.SearchAsync("alpha", 2, 2, default);

        firstPage.Tasks.Items.Should().HaveCount(2);
        firstPage.Tasks.TotalCount.Should().Be(3);
        secondPage.Tasks.Items.Should().HaveCount(1);
        secondPage.Tasks.TotalCount.Should().Be(3);
        firstPage.Tasks.Items.Select(t => t.Id).Should().NotIntersectWith(secondPage.Tasks.Items.Select(t => t.Id));
    }

    [Fact]
    public async Task SearchAsync_only_reflects_the_current_tenants_data()
    {
        var otherTenant = new Tenant { Name = "Other Tenant" };
        var otherProject = new Project { TenantId = otherTenant.Id, Tenant = otherTenant, Name = "Apollo Rockets" };
        var otherTask = new TaskItem { TenantId = otherTenant.Id, Tenant = otherTenant, ProjectId = otherProject.Id, Project = otherProject, Title = "Apollo launch task" };
        var otherUser = new User { Email = "apollo-other@example.com", PasswordHash = "unused" };
        var otherMember = new TenantMember { TenantId = otherTenant.Id, Tenant = otherTenant, UserId = otherUser.Id, User = otherUser, Role = TenantRole.Owner };
        DbContext.AddRange(otherTenant, otherProject, otherTask, otherUser, otherMember);
        await DbContext.SaveChangesAsync();

        var results = await _service.SearchAsync("apollo", 1, 10, default);

        results.Projects.Items.Should().ContainSingle(p => p.Id == _project.Id);
        results.Tasks.Items.Should().BeEmpty();
        results.Users.Items.Should().BeEmpty();
    }
}
