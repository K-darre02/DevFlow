using DevFlow.Application.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Tasks;

// Only validates what DataAnnotations on the Api-layer request DTO
// structurally cannot: whether referenced entities actually exist. Because
// _context.Projects/_context.Users are tenant-filtered (DevFlowDbContext),
// these checks also enforce "belongs to my tenant" for free — the same
// mechanism, not a second one bolted on for validation's sake.
public class CreateTaskInputValidator : AbstractValidator<CreateTaskInput>
{
    public CreateTaskInputValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.ProjectId)
            .MustAsync((projectId, ct) => context.Projects.AnyAsync(p => p.Id == projectId, ct))
            .WithMessage("Project does not exist or is not accessible.");

        RuleFor(x => x.AssigneeUserId)
            .MustAsync(async (assigneeUserId, ct) =>
                assigneeUserId is null || await context.Users.AnyAsync(u => u.Id == assigneeUserId, ct))
            .WithMessage("Assignee does not exist or is not accessible.");
    }
}
