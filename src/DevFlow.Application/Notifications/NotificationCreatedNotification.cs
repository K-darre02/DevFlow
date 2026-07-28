using MediatR;

namespace DevFlow.Application.Notifications;

/// <summary>
/// Published after a Notification row is committed (NotificationWriter) —
/// the Api layer's SignalR handler subscribes to this and forwards it to
/// exactly the one user it's for (TaskHub's per-user group), never
/// tenant-wide. This is a second-hop event: TaskAssignedNotification (etc.)
/// triggers a Notification row to be written, which in turn publishes this.
/// </summary>
public record NotificationCreatedNotification(Domain.Entities.Notification Notification) : INotification;
