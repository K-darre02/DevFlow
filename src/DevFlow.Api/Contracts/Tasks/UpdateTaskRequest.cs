using System.ComponentModel.DataAnnotations;
using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Tasks;

// Every field optional — PATCH semantics. See
// DevFlow.Application.Tasks.UpdateTaskInput for the "null = no change,
// can't explicitly clear a value" limitation this carries through to.
public record UpdateTaskRequest
{
    [MaxLength(500)]
    public string? Title { get; init; }

    [MaxLength(4000)]
    public string? Description { get; init; }

    public TaskItemStatus? Status { get; init; }

    public TaskPriority? Priority { get; init; }

    public Guid? AssigneeUserId { get; init; }

    public DateOnly? DueDate { get; init; }
}
