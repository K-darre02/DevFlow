namespace DevFlow.Application.Common.Exceptions;

/// <summary>
/// Thrown when an update's expected Version doesn't match the row's current
/// Version at save time — see docs/devflow/06-engineering-challenges.md §2.
/// Carries the current state so the caller (a controller) can return it in a
/// 409 response without a second round-trip.
/// </summary>
public class ConcurrencyConflictException<T> : Exception
{
    public T CurrentState { get; }

    public ConcurrencyConflictException(T currentState)
        : base("The resource was modified by another request.")
    {
        CurrentState = currentState;
    }
}
