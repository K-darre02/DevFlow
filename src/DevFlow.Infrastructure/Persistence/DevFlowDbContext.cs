using Microsoft.EntityFrameworkCore;

namespace DevFlow.Infrastructure.Persistence;

public class DevFlowDbContext : DbContext
{
    public DevFlowDbContext(DbContextOptions<DevFlowDbContext> options)
        : base(options)
    {
    }

    // DbSets and entity configurations are added as domain entities are introduced
    // in later phases — see docs/devflow/02-database-design.md for the target schema.
}
