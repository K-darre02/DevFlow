using DevFlow.Application.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.Application.Attachments;

// Same DB-aware-check-via-FluentValidation pattern as CreateTaskInputValidator:
// TaskId's existence check is tenant-scoped for free via context.TaskItems'
// query filter, so it doubles as the "task access" check the download flow
// also needs (AttachmentService applies the same check there directly).
public class UploadAttachmentInputValidator : AbstractValidator<UploadAttachmentInput>
{
    // A denylist has to anticipate every dangerous type; an allowlist only
    // has to list what's actually needed — safer default for
    // user-uploaded, potentially-executable content.
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
        "application/pdf",
        "text/plain", "text/csv",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/zip",
    };

    public const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    public UploadAttachmentInputValidator(IApplicationDbContext context)
    {
        RuleFor(x => x.TaskId)
            .MustAsync((taskId, ct) => context.TaskItems.AnyAsync(t => t.Id == taskId, ct))
            .WithMessage("Task does not exist or is not accessible.");

        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.ContentType)
            .Must(AllowedContentTypes.Contains)
            .WithMessage("This file type is not allowed.");

        RuleFor(x => x.Size)
            .GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(MaxSizeBytes).WithMessage($"The file exceeds the {MaxSizeBytes / (1024 * 1024)} MB size limit.");
    }
}
