using System.ComponentModel.DataAnnotations;
using DevFlow.Domain.Enums;

namespace DevFlow.Api.Contracts.Tasks;

public record CreateTaskRequest
{
    [Required]
    public Guid ProjectId { get; init; }

    [Required]
    [MaxLength(500)]
    public string Title { get; init; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; init; }

    public TaskPriority Priority { get; init; } = TaskPriority.Medium;

    public Guid? AssigneeUserId { get; init; }

    public DateOnly? DueDate { get; init; }
}
