namespace DevFlow.Api.Contracts.Attachments;

public record AttachmentResponse(
    Guid Id,
    Guid TaskId,
    string FileName,
    string ContentType,
    long Size,
    Guid UploadedByUserId,
    /// <summary>Resolved via a join — null on the immediate upload response (see TaskAttachmentsController.Upload), populated everywhere else.</summary>
    string? UploadedByEmail,
    DateTimeOffset CreatedAt);
