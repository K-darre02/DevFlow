using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using MediatR;

namespace DevFlow.Application.Realtime;

// Published by TaskService only after SaveChangesAsync has committed
// successfully — see the try/catch around each Publish call there. The Api
// layer (which owns SignalR) subscribes via INotificationHandler<T> and maps
// these onto the wire format; Application itself has no notion of hubs,
// groups, or transport, only "this happened."

public record TaskCreatedNotification(TaskItem Task) : INotification;

/// <summary>A field other than Status/AssigneeUserId changed (title, description, priority, due date).</summary>
public record TaskUpdatedNotification(TaskItem Task) : INotification;

public record TaskMovedNotification(TaskItem Task, TaskItemStatus FromStatus) : INotification;

public record TaskAssignedNotification(TaskItem Task) : INotification;

/// <summary>Status transitioned into Done. Fired alongside TaskMovedNotification when the move itself lands on Done.</summary>
public record TaskCompletedNotification(TaskItem Task) : INotification;

public record TaskDeletedNotification(TaskItem Task) : INotification;
