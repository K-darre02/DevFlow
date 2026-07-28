using DevFlow.Application.Search;

namespace DevFlow.Api.Contracts.Search;

public static class SearchResponseMapper
{
    public static SearchResponse ToResponse(SearchResults results) => new(
        ToGroupResponse(results.Projects, ToResponse),
        ToGroupResponse(results.Tasks, ToResponse),
        ToGroupResponse(results.Users, ToResponse));

    private static SearchResultGroupResponse<TResponse> ToGroupResponse<TResult, TResponse>(
        SearchResultGroup<TResult> group, Func<TResult, TResponse> map) =>
        new(group.Items.Select(map).ToList(), group.TotalCount);

    private static ProjectSearchResultResponse ToResponse(ProjectSearchResult result) => new(
        result.Id, result.Name, result.IsArchived, result.Rank);

    private static TaskSearchResultResponse ToResponse(TaskSearchResult result) => new(
        result.Id, result.Title, result.Description, result.ProjectId, result.ProjectName, result.Status, result.Rank);

    private static UserSearchResultResponse ToResponse(UserSearchResult result) => new(
        result.UserId, result.Email, result.Role, result.Rank);
}
