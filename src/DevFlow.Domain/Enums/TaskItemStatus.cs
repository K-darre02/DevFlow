namespace DevFlow.Domain.Enums;

// Named TaskItemStatus rather than TaskStatus to avoid colliding with
// System.Threading.Tasks.TaskStatus, which is implicitly in scope on every
// project in this solution (ImplicitUsings includes System.Threading.Tasks).
public enum TaskItemStatus
{
    Backlog = 0,
    ToDo = 1,
    InProgress = 2,
    InReview = 3,
    Done = 4
}
