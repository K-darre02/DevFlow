namespace DevFlow.Application.Attachments;

/// <summary>The file's own bytes are deliberately not part of this record — only what needs validating does. See AttachmentService.UploadAsync for the Stream parameter.</summary>
public record UploadAttachmentInput(Guid TaskId, string FileName, string ContentType, long Size);
