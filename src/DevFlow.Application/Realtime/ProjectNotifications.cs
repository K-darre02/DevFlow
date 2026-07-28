using DevFlow.Domain.Entities;
using MediatR;

namespace DevFlow.Application.Realtime;

public record ProjectCreatedNotification(Project Project) : INotification;

/// <summary>Fired only on the specific not-archived -> archived transition, not on every update.</summary>
public record ProjectArchivedNotification(Project Project) : INotification;
