using DevFlow.Api.Contracts.Attachments;
using DevFlow.Application.Attachments;
using DevFlow.Application.Common;
using DevFlow.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

// Any authenticated tenant member can upload/view/download/delete an
// attachment — the same permission model TasksController itself uses for
// task create/update/delete (no extra role gate here beyond what task
// mutations already require), since attachments are just task content.
// Tenant isolation and "verify task access" both come from
// AttachmentService's use of the ambient query filter + explicit TaskId
// check — see FindAttachmentAsync there.
[ApiController]
[Route("api/tasks/{taskId}/attachments")]
[Authorize]
public class TaskAttachmentsController : ControllerBase
{
    private readonly IAttachmentService _attachmentService;
    private readonly ICurrentUserService _currentUserService;

    public TaskAttachmentsController(IAttachmentService attachmentService, ICurrentUserService currentUserService)
    {
        _attachmentService = attachmentService;
        _currentUserService = currentUserService;
    }

    // Hard upper bound (above the 10 MB UploadAttachmentInputValidator
    // actually enforces) so a wildly oversized request is rejected before
    // it's ever buffered into memory/disk, not just after — "size limits
    // enforced" at the transport layer, not only the validation layer.
    [HttpPost]
    [RequestSizeLimit(15_000_000)]
    public async Task<ActionResult<AttachmentResponse>> Upload(Guid taskId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "A file is required.");
        }

        var tenantId = _currentUserService.TenantId!.Value;
        var userId = _currentUserService.UserId!.Value;

        // Path.GetFileName strips any client-supplied directory portion —
        // most modern browsers already send just the base name, but this
        // isn't guaranteed by the multipart spec, and FileName ends up
        // stored and later shown back to users verbatim.
        var fileName = Path.GetFileName(file.FileName);
        var input = new UploadAttachmentInput(taskId, fileName, file.ContentType, file.Length);

        await using var stream = file.OpenReadStream();
        var attachment = await _attachmentService.UploadAsync(tenantId, userId, input, stream, cancellationToken);

        return CreatedAtAction(nameof(GetAttachments), new { taskId }, ToResponse(attachment));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AttachmentResponse>>> GetAttachments(Guid taskId, CancellationToken cancellationToken)
    {
        var attachments = await _attachmentService.GetAttachmentsAsync(taskId, cancellationToken);
        return Ok(attachments.Select(ToResponse));
    }

    // Redirects (302) to a short-lived signed URL rather than streaming the
    // file in this response directly — see IBlobStorageService.GetDownloadUrlAsync.
    [HttpGet("{attachmentId}/download")]
    public async Task<IActionResult> Download(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var url = await _attachmentService.GetDownloadUrlAsync(taskId, attachmentId, cancellationToken);
        return url is null ? NotFound() : Redirect(url.ToString());
    }

    [HttpDelete("{attachmentId}")]
    public async Task<IActionResult> Delete(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var deleted = await _attachmentService.DeleteAsync(taskId, attachmentId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static AttachmentResponse ToResponse(TaskAttachment attachment) => new(
        attachment.Id,
        attachment.TaskId,
        attachment.FileName,
        attachment.ContentType,
        attachment.Size,
        attachment.UploadedByUserId,
        attachment.UploadedByUser?.Email,
        attachment.CreatedAt);
}
