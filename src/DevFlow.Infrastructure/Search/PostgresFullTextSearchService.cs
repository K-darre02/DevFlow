using DevFlow.Application.Common;
using DevFlow.Application.Search;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Infrastructure.Search;

/// <summary>
/// Production ISearchService — real PostgreSQL full-text search
/// (to_tsvector/plainto_tsquery/ts_rank) via Npgsql's EF Core provider
/// (EF.Functions.ToTsVector/PlainToTsQuery + NpgsqlTsVector.Matches/Rank).
/// This is ordinary LINQ against IApplicationDbContext, not raw SQL, so it
/// automatically inherits the same tenant query filters every other query in
/// the system goes through (see DevFlowDbContext.OnModelCreating) rather
/// than needing its own hand-written WHERE "TenantId" = ... clause.
///
/// Not exercised by this solution's automated tests: this sandbox has no
/// Docker/Postgres, and Sqlite (what tests actually run against) has no
/// translation path for these Npgsql-specific functions at all — see
/// LikeSearchService, the substitute the test suite exercises instead.
/// Verified here only by compiling against the real
/// Npgsql.EntityFrameworkCore.PostgreSQL package (which does catch wrong
/// method names/signatures); running this against a live PostgreSQL
/// instance is a required step before trusting it in production.
/// </summary>
public class PostgresFullTextSearchService : ISearchService
{
    private const string TextSearchConfig = "english";

    private readonly IApplicationDbContext _context;

    public PostgresFullTextSearchService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SearchResults> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return new SearchResults(
                new SearchResultGroup<ProjectSearchResult>([], 0),
                new SearchResultGroup<TaskSearchResult>([], 0),
                new SearchResultGroup<UserSearchResult>([], 0));
        }

        var clampedPage = Math.Max(page, 1);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);

        var projects = await SearchProjectsAsync(term, clampedPage, clampedPageSize, cancellationToken);
        var tasks = await SearchTasksAsync(term, clampedPage, clampedPageSize, cancellationToken);
        var users = await SearchUsersAsync(term, clampedPage, clampedPageSize, cancellationToken);

        return new SearchResults(projects, tasks, users);
    }

    private async Task<SearchResultGroup<ProjectSearchResult>> SearchProjectsAsync(
        string term, int page, int pageSize, CancellationToken cancellationToken)
    {
        var matches = _context.Projects.Where(p =>
            EF.Functions.ToTsVector(TextSearchConfig, p.Name)
                .Matches(EF.Functions.PlainToTsQuery(TextSearchConfig, term)));

        var totalCount = await matches.CountAsync(cancellationToken);

        var items = await matches
            .OrderByDescending(p =>
                EF.Functions.ToTsVector(TextSearchConfig, p.Name).Rank(EF.Functions.PlainToTsQuery(TextSearchConfig, term)))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProjectSearchResult(
                p.Id,
                p.Name,
                p.IsArchived,
                EF.Functions.ToTsVector(TextSearchConfig, p.Name).Rank(EF.Functions.PlainToTsQuery(TextSearchConfig, term))))
            .ToListAsync(cancellationToken);

        return new SearchResultGroup<ProjectSearchResult>(items, totalCount);
    }

    private async Task<SearchResultGroup<TaskSearchResult>> SearchTasksAsync(
        string term, int page, int pageSize, CancellationToken cancellationToken)
    {
        var matches = _context.TaskItems.Where(t =>
            EF.Functions.ToTsVector(TextSearchConfig, t.Title + " " + (t.Description ?? ""))
                .Matches(EF.Functions.PlainToTsQuery(TextSearchConfig, term)));

        var totalCount = await matches.CountAsync(cancellationToken);

        var items = await matches
            .OrderByDescending(t =>
                EF.Functions.ToTsVector(TextSearchConfig, t.Title + " " + (t.Description ?? ""))
                    .Rank(EF.Functions.PlainToTsQuery(TextSearchConfig, term)))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TaskSearchResult(
                t.Id,
                t.Title,
                t.Description,
                t.ProjectId,
                t.Project.Name,
                t.Status,
                EF.Functions.ToTsVector(TextSearchConfig, t.Title + " " + (t.Description ?? ""))
                    .Rank(EF.Functions.PlainToTsQuery(TextSearchConfig, term))))
            .ToListAsync(cancellationToken);

        return new SearchResultGroup<TaskSearchResult>(items, totalCount);
    }

    private async Task<SearchResultGroup<UserSearchResult>> SearchUsersAsync(
        string term, int page, int pageSize, CancellationToken cancellationToken)
    {
        // TenantMembers, never Users directly — User is a global entity with
        // no tenant scoping (see User.cs); TenantMembers carries the ambient
        // tenant query filter that keeps this from leaking other tenants'
        // users.
        var matches = _context.TenantMembers.Where(m =>
            EF.Functions.ToTsVector(TextSearchConfig, m.User.Email)
                .Matches(EF.Functions.PlainToTsQuery(TextSearchConfig, term)));

        var totalCount = await matches.CountAsync(cancellationToken);

        var items = await matches
            .OrderByDescending(m =>
                EF.Functions.ToTsVector(TextSearchConfig, m.User.Email).Rank(EF.Functions.PlainToTsQuery(TextSearchConfig, term)))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new UserSearchResult(
                m.UserId,
                m.User.Email,
                m.Role,
                EF.Functions.ToTsVector(TextSearchConfig, m.User.Email).Rank(EF.Functions.PlainToTsQuery(TextSearchConfig, term))))
            .ToListAsync(cancellationToken);

        return new SearchResultGroup<UserSearchResult>(items, totalCount);
    }
}
