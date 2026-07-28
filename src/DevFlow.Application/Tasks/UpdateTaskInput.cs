using DevFlow.Domain.Enums;

namespace DevFlow.Application.Tasks;

// Every field is "if provided (non-null), set it; if null, leave unchanged" —
// simple PATCH semantics with a known limitation: there's no way to explicitly
// clear Description/AssigneeUserId/DueDate back to null through this endpoint,
// only to change them to a new non-null value. A sentinel/"field presence"
// scheme would fix that but isn't warranted yet — flagged as a deliberate
// simplification, not an oversight.
public record UpdateTaskInput(
    string? Title,
    string? Description,
    TaskItemStatus? Status,
    TaskPriority? Priority,
    Guid? AssigneeUserId,
    DateOnly? DueDate);
