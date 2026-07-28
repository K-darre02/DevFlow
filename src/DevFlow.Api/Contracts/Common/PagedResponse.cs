namespace DevFlow.Api.Contracts.Common;

public record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
