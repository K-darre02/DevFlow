using MediatR;

namespace DevFlow.IntegrationTests.TestSupport;

// A real MediatR IPublisher is overkill for service-level tests (it would
// need handlers registered, which live in the Api project) — this just
// records what got published, so tests can assert "the right notification
// fired after a successful write" / "no notification fired on failure"
// without depending on Api's SignalR handlers at all.
public sealed class RecordingPublisher : IPublisher
{
    public List<INotification> Published { get; } = new();

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        if (notification is INotification typed)
        {
            Published.Add(typed);
        }

        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }
}
