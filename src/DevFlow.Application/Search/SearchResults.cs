using DevFlow.Domain.Enums;

namespace DevFlow.Application.Search;

public record ProjectSearchResult(Guid Id, string Name, bool IsArchived, double Rank);

public record TaskSearchResult(
    Guid Id,
    string Title,
    string? Description,
    Guid ProjectId,
    string ProjectName,
    TaskItemStatus Status,
    double Rank);

// UserId (TenantMember.UserId), not TenantMember.Id — the frontend needs the
// same identifier the rest of the app already uses to refer to a user (e.g.
// TaskItem.AssigneeUserId), not the join row's own id.
public record UserSearchResult(Guid UserId, string Email, TenantRole Role, double Rank);

/// <summary>
/// Rank-ordered, paginated matches for one entity category. TotalCount is
/// the full match count for this category before paging — enough for a
/// caller to render "N more results" without a second round trip.
/// </summary>
public record SearchResultGroup<T>(IReadOnlyList<T> Items, int TotalCount);

public record SearchResults(
    SearchResultGroup<ProjectSearchResult> Projects,
    SearchResultGroup<TaskSearchResult> Tasks,
    SearchResultGroup<UserSearchResult> Users);
