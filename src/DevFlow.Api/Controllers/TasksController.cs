using DevFlow.Api.Contracts.Tasks;
using DevFlow.Application.Common;
using DevFlow.Application.Common.Exceptions;
using DevFlow.Application.Tasks;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly ICurrentUserService _currentUserService;

    public TasksController(ITaskService taskService, ICurrentUserService currentUserService)
    {
        _taskService = taskService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskResponse>>> GetTasks(
        [FromQuery] Guid? projectId,
        [FromQuery] TaskItemStatus? status,
        [FromQuery] Guid? assigneeUserId,
        CancellationToken cancellationToken)
    {
        var tasks = await _taskService.GetTasksAsync(projectId, status, assigneeUserId, cancellationToken);
        return Ok(tasks.Select(ToResponse));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TaskResponse>> GetTask(Guid id, CancellationToken cancellationToken)
    {
        var task = await _taskService.GetTaskByIdAsync(id, cancellationToken);
        return task is null ? NotFound() : Ok(ToResponse(task));
    }

    // NotFoundException from the service (ProjectId/AssigneeUserId don't
    // resolve within the caller's tenant) is handled by GlobalExceptionHandler,
    // not here — see Middleware/GlobalExceptionHandler.cs.
    [HttpPost]
    public async Task<ActionResult<TaskResponse>> CreateTask(
        [FromBody] CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.TenantId!.Value;

        var input = new CreateTaskInput(
            request.ProjectId,
            request.Title,
            request.Description,
            request.Priority,
            request.AssigneeUserId,
            request.DueDate);

        var task = await _taskService.CreateTaskAsync(tenantId, input, cancellationToken);

        return CreatedAtAction(nameof(GetTask), new { id = task.Id }, ToResponse(task));
    }

    // Requires If-Match (the Version from a prior GET) — see
    // docs/devflow/06-engineering-challenges.md §2 and
    // docs/devflow/03-api-design.md §4 for the documented flow this
    // implements: optimistic concurrency, 409 with current state on conflict.
    [HttpPatch("{id}")]
    public async Task<ActionResult<TaskResponse>> UpdateTask(
        Guid id,
        [FromBody] UpdateTaskRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "If-Match header is required.");
        }

        if (!int.TryParse(ifMatch.Trim('"'), out var expectedVersion))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "If-Match header is not a valid version.");
        }

        var input = new UpdateTaskInput(
            request.Title,
            request.Description,
            request.Status,
            request.Priority,
            request.AssigneeUserId,
            request.DueDate);

        try
        {
            var task = await _taskService.UpdateTaskAsync(id, input, expectedVersion, cancellationToken);
            return task is null ? NotFound() : Ok(ToResponse(task));
        }
        catch (ConcurrencyConflictException<TaskItem> ex)
        {
            return Conflict(new
            {
                type = "https://devflow.dev/errors/conflict",
                title = "Task was modified by another request",
                status = StatusCodes.Status409Conflict,
                detail = "The task's data has changed since it was last fetched.",
                current = ToResponse(ex.CurrentState)
            });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _taskService.DeleteTaskAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static TaskResponse ToResponse(TaskItem task) => TaskResponseMapper.ToResponse(task);
}
