using DevFlow.Application.Common;
using DevFlow.Application.Common.Exceptions;
using DevFlow.Application.Realtime;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevFlow.Application.Tasks;

public class TaskService : ITaskService
{
    private readonly IApplicationDbContext _context;
    private readonly IValidator<CreateTaskInput> _createValidator;
    private readonly IValidator<UpdateTaskInput> _updateValidator;
    private readonly IPublisher _publisher;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        IApplicationDbContext context,
        IValidator<CreateTaskInput> createValidator,
        IValidator<UpdateTaskInput> updateValidator,
        IPublisher publisher,
        ILogger<TaskService> logger)
    {
        _context = context;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(
        Guid? projectId,
        TaskItemStatus? status,
        Guid? assigneeUserId,
        CancellationToken cancellationToken)
    {
        var query = _context.TaskItems.AsNoTracking().AsQueryable();

        if (projectId is not null)
        {
            query = query.Where(t => t.ProjectId == projectId);
        }

        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        if (assigneeUserId is not null)
        {
            query = query.Where(t => t.AssigneeUserId == assigneeUserId);
        }

        return await query.OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<TaskItem?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<TaskItem> CreateTaskAsync(Guid tenantId, CreateTaskInput input, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(input, cancellationToken);

        var task = new TaskItem
        {
            TenantId = tenantId,
            ProjectId = input.ProjectId,
            Title = input.Title,
            Description = input.Description,
            Priority = input.Priority,
            AssigneeUserId = input.AssigneeUserId,
            DueDate = input.DueDate
        };

        _context.TaskItems.Add(task);
        await _context.SaveChangesAsync(cancellationToken);

        await PublishSafeAsync(new TaskCreatedNotification(task), cancellationToken);

        // Assigning on creation is just as much an assignment as assigning
        // via a later PATCH — the assignee should be notified either way,
        // not only when AssigneeUserId changes on an existing task.
        if (task.AssigneeUserId is not null)
        {
            await PublishSafeAsync(new TaskAssignedNotification(task), cancellationToken);
        }

        await PublishMentionsIfAnyAsync(task, input.Description, cancellationToken);

        return task;
    }

    public async Task<TaskItem?> UpdateTaskAsync(
        Guid id,
        UpdateTaskInput input,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(input, cancellationToken);

        var task = await _context.TaskItems.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (task is null)
        {
            return null;
        }

        // Tells EF Core what the caller believes the current row looks like;
        // the UPDATE statement's WHERE clause includes this, so a save only
        // succeeds if nothing else changed the row first. This is the actual
        // enforcement — not the plain comparison of expectedVersion above,
        // which would leave a race window between reading and saving.
        SetExpectedVersion(task, expectedVersion);

        var previousStatus = task.Status;
        var previousAssigneeUserId = task.AssigneeUserId;

        if (input.Title is not null)
        {
            task.Title = input.Title;
        }

        if (input.Description is not null)
        {
            task.Description = input.Description;
        }

        if (input.Priority is not null)
        {
            task.Priority = input.Priority.Value;
        }

        if (input.AssigneeUserId is not null)
        {
            task.AssigneeUserId = input.AssigneeUserId;
        }

        if (input.DueDate is not null)
        {
            task.DueDate = input.DueDate;
        }

        if (input.Status is not null)
        {
            task.Status = input.Status.Value;
            task.CompletedAt = task.Status == TaskItemStatus.Done ? DateTimeOffset.UtcNow : null;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await _context.TaskItems.AsNoTracking().FirstAsync(t => t.Id == id, cancellationToken);
            throw new ConcurrencyConflictException<TaskItem>(current);
        }

        // Granular, semantic events rather than one always-fired "updated" —
        // a single PATCH can trigger more than one of these (e.g. moving a
        // task straight to Done fires both Moved and Completed).
        var statusChanged = input.Status is not null && input.Status.Value != previousStatus;
        var assigneeChanged = input.AssigneeUserId is not null && input.AssigneeUserId != previousAssigneeUserId;
        var otherFieldsChanged = input.Title is not null || input.Description is not null
            || input.Priority is not null || input.DueDate is not null;

        if (statusChanged)
        {
            await PublishSafeAsync(new TaskMovedNotification(task, previousStatus), cancellationToken);
        }

        if (task.Status == TaskItemStatus.Done && previousStatus != TaskItemStatus.Done)
        {
            await PublishSafeAsync(new TaskCompletedNotification(task), cancellationToken);
        }

        if (assigneeChanged)
        {
            await PublishSafeAsync(new TaskAssignedNotification(task), cancellationToken);
        }

        if (otherFieldsChanged)
        {
            await PublishSafeAsync(new TaskUpdatedNotification(task), cancellationToken);
        }

        // Only when the caller explicitly changed Description in *this*
        // request — not on every update, which would re-notify people
        // already mentioned in an otherwise-untouched description every
        // time someone e.g. changes the priority.
        if (input.Description is not null)
        {
            await PublishMentionsIfAnyAsync(task, input.Description, cancellationToken);
        }

        return task;
    }

    public async Task<bool> DeleteTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await _context.TaskItems.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (task is null)
        {
            return false;
        }

        _context.TaskItems.Remove(task);
        await _context.SaveChangesAsync(cancellationToken);

        await PublishSafeAsync(new TaskDeletedNotification(task), cancellationToken);

        return true;
    }

    private async Task PublishMentionsIfAnyAsync(TaskItem task, string? description, CancellationToken cancellationToken)
    {
        var mentionedUserIds = await MentionParser.ResolveMentionedUserIdsAsync(_context, description, cancellationToken);

        if (mentionedUserIds.Count > 0)
        {
            await PublishSafeAsync(new TaskMentionedNotification(task, mentionedUserIds), cancellationToken);
        }
    }

    // Swallows and logs rather than rethrowing: a broadcast failure (e.g. a
    // transient SignalR issue) must never turn an already-committed,
    // otherwise-successful write into a 500 for the caller.
    private async Task PublishSafeAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {NotificationType} after a successful task write", notification.GetType().Name);
        }
    }

    private void SetExpectedVersion(TaskItem task, int expectedVersion)
    {
        // IApplicationDbContext doesn't expose ChangeTracker.Entry (that's an
        // EF Core / DbContext-specific API, not something Application should
        // depend on) — DevFlowDbContext is the only real implementation, so
        // this casts to it deliberately rather than widening the interface
        // for one call site.
        if (_context is DbContext dbContext)
        {
            dbContext.Entry(task).Property(nameof(TaskItem.Version)).OriginalValue = expectedVersion;
        }
    }
}
