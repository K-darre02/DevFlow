namespace DevFlow.Application.Common.Exceptions;

/// <summary>
/// Thrown when a write operation references another entity (a ProjectId, an
/// AssigneeUserId, ...) that doesn't exist — or, thanks to the tenant query
/// filter, doesn't exist *for this tenant*, which is indistinguishable from
/// not existing at all from the caller's side. Not used for the common
/// "GET by id, might not exist" case — those return null and the controller
/// maps that to 404 directly, no exception needed.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.")
    {
    }
}
