using DevFlow.Api.Contracts.Search;
using DevFlow.Application.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

// Any authenticated tenant member can search — same read-is-not-role-gated
// precedent as ActivityController/DashboardController/TeamController.GetTeam.
// Tenant isolation (including keeping the Users group scoped to
// TenantMembers rather than the global Users table) is enforced inside
// ISearchService, not here.
[ApiController]
[Route("api/search")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromQuery] string? q,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        // A missing/blank q is a valid request for zero results, not a
        // client error — ISearchService.SearchAsync short-circuits it to
        // empty groups rather than querying anything.
        var results = await _searchService.SearchAsync(q ?? string.Empty, page ?? 1, pageSize ?? 10, cancellationToken);

        return Ok(SearchResponseMapper.ToResponse(results));
    }
}
