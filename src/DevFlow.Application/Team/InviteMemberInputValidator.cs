using DevFlow.Application.Common;
using DevFlow.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Team;

// Duplicate-invitation and existing-member checks need the database — same
// DB-aware-check-via-FluentValidation pattern as CreateTaskInputValidator.
// Both queries below are scoped to the *current* tenant automatically via
// DevFlowDbContext's global query filter (context.TenantMembers /
// context.Invitations), not by anything in this validator.
public class InviteMemberInputValidator : AbstractValidator<InviteMemberInput>
{
    public InviteMemberInputValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Role)
            .Must(role => role is TenantRole.Member or TenantRole.Admin)
            .WithMessage("Invitations can only grant the Member or Admin role.");

        RuleFor(x => x.Email)
            .MustAsync(async (email, ct) => !await context.TenantMembers.AnyAsync(m => m.User.Email == email, ct))
            .WithMessage("This person is already a member of this workspace.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Email)
            .MustAsync(async (email, ct) =>
            {
                // The final ">" comparison runs in memory rather than as
                // part of the query: SQLite's EF Core provider (used by this
                // solution's tests) only translates equality on
                // DateTimeOffset, not relational operators — Npgsql handles
                // it natively, but the query has to stay provider-agnostic.
                // Narrowed by Email + AcceptedAt first, so this is at most a
                // couple of rows in practice, not a full table scan.
                var now = DateTimeOffset.UtcNow;
                var pendingExpirations = await context.Invitations
                    .Where(i => i.Email == email && i.AcceptedAt == null)
                    .Select(i => i.ExpiresAt)
                    .ToListAsync(ct);
                return !pendingExpirations.Any(expiresAt => expiresAt > now);
            })
            .WithMessage("There is already a pending invitation for this email.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
