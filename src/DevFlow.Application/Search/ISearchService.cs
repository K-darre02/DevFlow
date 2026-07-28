namespace DevFlow.Application.Search;

/// <summary>
/// Keyword search across Projects, Tasks, and Users, scoped to the caller's
/// tenant. Implemented by PostgresFullTextSearchService (DevFlow.Infrastructure —
/// the production backend, real tsvector/ts_rank full-text search) and
/// LikeSearchService (DevFlow.Application — the portable substitute used
/// wherever the Npgsql provider isn't the active one, e.g. this solution's
/// Sqlite-backed automated tests; see docs/devflow/01-architecture.md §9's
/// table of local stand-ins for cloud/engine-specific services, and the same
/// dual-implementation shape already used for IBlobStorageService).
/// </summary>
public interface ISearchService
{
    Task<SearchResults> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken);
}
