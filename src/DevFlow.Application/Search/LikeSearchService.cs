using DevFlow.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Search;

/// <summary>
/// LIKE-based ISearchService — the portable substitute for PostgresFullTextSearchService
/// (see ISearchService's doc comment). Not genuine full-text search: no
/// stemming, no stopword handling, no tsvector/GIN index. Ranking is a
/// coarse, deterministic heuristic (does the term prefix-match, substring-match,
/// or only match a secondary field) rather than ts_rank's real relevance
/// scoring — good enough to prove the ISearchService contract (matching,
/// ordering, tenant isolation, pagination) end-to-end against Sqlite, which
/// has no translation path for Postgres' full-text functions at all.
/// </summary>
public class LikeSearchService : ISearchService
{
    // Bounds how many LIKE-matched rows per category get pulled into memory
    // to be ranked and paged — generous relative to realistic page sizes,
    // and only reachable in the substitute implementation this comment lives
    // in (production's PostgresFullTextSearchService pages entirely in SQL).
    private const int CandidateCap = 200;

    private const double PrefixMatchRank = 3.0;
    private const double ContainsMatchRank = 2.0;
    private const double SecondaryFieldMatchRank = 1.0;

    private readonly IApplicationDbContext _context;

    public LikeSearchService(IApplicationDbContext context)
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

        var loweredTerm = term.ToLowerInvariant();
        var clampedPage = Math.Max(page, 1);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);

        var projects = await SearchProjectsAsync(loweredTerm, clampedPage, clampedPageSize, cancellationToken);
        var tasks = await SearchTasksAsync(loweredTerm, clampedPage, clampedPageSize, cancellationToken);
        var users = await SearchUsersAsync(loweredTerm, clampedPage, clampedPageSize, cancellationToken);

        return new SearchResults(projects, tasks, users);
    }

    private async Task<SearchResultGroup<ProjectSearchResult>> SearchProjectsAsync(
        string loweredTerm, int page, int pageSize, CancellationToken cancellationToken)
    {
        var matches = _context.Projects.Where(p => EF.Functions.Like(p.Name.ToLower(), $"%{loweredTerm}%"));

        var totalCount = await matches.CountAsync(cancellationToken);

        var candidates = await matches
            .AsNoTracking()
            .Take(CandidateCap)
            .Select(p => new { p.Id, p.Name, p.IsArchived })
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(p => new ProjectSearchResult(p.Id, p.Name, p.IsArchived, Rank(p.Name, loweredTerm)))
            .OrderByDescending(r => r.Rank)
            .ThenBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new SearchResultGroup<ProjectSearchResult>(ranked, totalCount);
    }

    private async Task<SearchResultGroup<TaskSearchResult>> SearchTasksAsync(
        string loweredTerm, int page, int pageSize, CancellationToken cancellationToken)
    {
        var matches = _context.TaskItems.Where(t =>
            EF.Functions.Like(t.Title.ToLower(), $"%{loweredTerm}%") ||
            (t.Description != null && EF.Functions.Like(t.Description.ToLower(), $"%{loweredTerm}%")));

        var totalCount = await matches.CountAsync(cancellationToken);

        var candidates = await matches
            .AsNoTracking()
            .Include(t => t.Project)
            .Take(CandidateCap)
            .Select(t => new { t.Id, t.Title, t.Description, t.ProjectId, ProjectName = t.Project.Name, t.Status })
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(t => new TaskSearchResult(
                t.Id, t.Title, t.Description, t.ProjectId, t.ProjectName, t.Status,
                Rank(t.Title, loweredTerm, t.Description)))
            .OrderByDescending(r => r.Rank)
            .ThenBy(r => r.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new SearchResultGroup<TaskSearchResult>(ranked, totalCount);
    }

    private async Task<SearchResultGroup<UserSearchResult>> SearchUsersAsync(
        string loweredTerm, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Users is a global entity with no tenant scoping (see User.cs) —
        // searching it directly would leak users from every tenant.
        // TenantMembers is the tenant-scoped join and carries the ambient
        // query filter, so it's the only safe path to "users in my tenant".
        var matches = _context.TenantMembers
            .Include(m => m.User)
            .Where(m => EF.Functions.Like(m.User.Email.ToLower(), $"%{loweredTerm}%"));

        var totalCount = await matches.CountAsync(cancellationToken);

        var candidates = await matches
            .AsNoTracking()
            .Take(CandidateCap)
            .Select(m => new { m.UserId, Email = m.User.Email, m.Role })
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(m => new UserSearchResult(m.UserId, m.Email, m.Role, Rank(m.Email, loweredTerm)))
            .OrderByDescending(r => r.Rank)
            .ThenBy(r => r.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new SearchResultGroup<UserSearchResult>(ranked, totalCount);
    }

    private static double Rank(string primaryField, string loweredTerm, string? secondaryField = null)
    {
        var loweredPrimary = primaryField.ToLowerInvariant();
        if (loweredPrimary.StartsWith(loweredTerm, StringComparison.Ordinal))
        {
            return PrefixMatchRank;
        }

        if (loweredPrimary.Contains(loweredTerm, StringComparison.Ordinal))
        {
            return ContainsMatchRank;
        }

        return SecondaryFieldMatchRank;
    }
}
