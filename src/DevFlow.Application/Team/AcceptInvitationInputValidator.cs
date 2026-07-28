using DevFlow.Application.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Team;

public class AcceptInvitationInputValidator : AbstractValidator<AcceptInvitationInput>
{
    public AcceptInvitationInputValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);

        // A single lookup with tailored messages per failure reason, rather
        // than three separate MustAsync rules that would each re-query —
        // "not found" / "expired" / "already used" are mutually exclusive
        // outcomes of the same query. IgnoreQueryFilters: there's no tenant
        // context yet — the token is how one gets established at all.
        RuleFor(x => x.Token).CustomAsync(async (token, validationContext, ct) =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                validationContext.AddFailure("Invitation token is required.");
                return;
            }

            var tokenHash = InvitationTokens.Hash(token);
            var invitation = await context.Invitations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

            if (invitation is null)
            {
                validationContext.AddFailure("Invalid invitation token.");
                return;
            }

            if (invitation.AcceptedAt is not null)
            {
                validationContext.AddFailure("This invitation has already been used.");
                return;
            }

            if (invitation.ExpiresAt < DateTimeOffset.UtcNow)
            {
                validationContext.AddFailure("This invitation has expired.");
            }
        });
    }
}
