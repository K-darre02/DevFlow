namespace DevFlow.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request passes its [Authorize] policy (the caller has the
/// right *role* to attempt this kind of operation) but fails a
/// domain-level authorization rule that depends on the specific data
/// involved — e.g. "an Admin can invite/remove members, but not remove an
/// Owner" or "a tenant can never be left with zero Owners". Those can't be
/// expressed as a static policy because they depend on the target member's
/// role or the tenant's current membership, not just the caller's own role.
/// </summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message)
        : base(message)
    {
    }
}
