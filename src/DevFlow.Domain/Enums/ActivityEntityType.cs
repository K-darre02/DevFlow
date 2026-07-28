namespace DevFlow.Domain.Enums;

// Named ActivityEntityType rather than EntityType to avoid a generic,
// easily-collided name in a namespace already shared by every other enum in
// the solution.
public enum ActivityEntityType
{
    Project = 0,
    Task = 1,
    Invitation = 2,
    TeamMember = 3
}
