using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Search;

public record SearchResultGroupResponse<T>(IReadOnlyList<T> Items, int TotalCount);

public record ProjectSearchResultResponse(Guid Id, string Name, bool IsArchived, double Rank);

public record TaskSearchResultResponse(
    Guid Id,
    string Title,
    string? Description,
    Guid ProjectId,
    string ProjectName,
    TaskItemStatus Status,
    double Rank);

public record UserSearchResultResponse(Guid UserId, string Email, TenantRole Role, double Rank);

public record SearchResponse(
    SearchResultGroupResponse<ProjectSearchResultResponse> Projects,
    SearchResultGroupResponse<TaskSearchResultResponse> Tasks,
    SearchResultGroupResponse<UserSearchResultResponse> Users);
