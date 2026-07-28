using DevFlow.Application.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Tasks;

public class UpdateTaskInputValidator : AbstractValidator<UpdateTaskInput>
{
    public UpdateTaskInputValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.AssigneeUserId)
            .MustAsync(async (assigneeUserId, ct) =>
                assigneeUserId is null || await context.Users.AnyAsync(u => u.Id == assigneeUserId, ct))
            .WithMessage("Assignee does not exist or is not accessible.");
    }
}
