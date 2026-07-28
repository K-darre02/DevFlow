using DevFlow.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DevFlow.IntegrationTests.TestSupport;

// A real (Sqlite in-memory) DevFlowDbContext rather than a mock — DbSet/LINQ
// query translation and the tenant query filters are the actual behavior
// under test in most of these tests, not something safe to fake out. Runs
// without Docker/Postgres, which this sandbox doesn't have.
public abstract class SqliteContextFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    protected readonly DevFlowDbContext DbContext;
    protected readonly FakeCurrentUserService CurrentUser;

    protected SqliteContextFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DevFlowDbContext>()
            .UseSqlite(_connection)
            .Options;

        CurrentUser = new FakeCurrentUserService();
        DbContext = new DevFlowDbContext(options, CurrentUser);
        DbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        DbContext.Dispose();
        _connection.Dispose();
    }
}
