using DevFlow.Domain.Common;

namespace DevFlow.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; } = new List<User>();

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
