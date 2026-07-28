using DevFlow.Application.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Tasks;

// See CreateTaskInputValidator for why this checks TenantMembers rather
// than Users directly.
public class UpdateTaskInputValidator : AbstractValidator<UpdateTaskInput>
{
    public UpdateTaskInputValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.AssigneeUserId)
            .MustAsync(async (assigneeUserId, ct) =>
                assigneeUserId is null || await context.TenantMembers.AnyAsync(m => m.UserId == assigneeUserId, ct))
            .WithMessage("Assignee does not exist or is not a member of this workspace.");
    }
}
