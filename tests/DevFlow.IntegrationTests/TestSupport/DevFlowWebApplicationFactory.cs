using System.Text;
using DevFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace DevFlow.IntegrationTests.TestSupport;

// Boots the real ASP.NET Core pipeline — [Authorize], JWT validation,
// tenant-resolution middleware via ICurrentUserService, routing, everything
// Program.cs wires up — against a Sqlite in-memory database instead of the
// real Npgsql one, so authorization/tenant-isolation behavior can be proven
// end-to-end (not just at the service layer) without Docker/Postgres.
public class DevFlowWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string JwtSigningKey = "test-signing-key-at-least-32-bytes-long-for-hmac-sha256";
    public const string JwtIssuer = "DevFlow.Tests";
    public const string JwtAudience = "DevFlow.Tests";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        // Prevents AddApiServices's eager `Jwt:SigningKey is not configured`
        // throw during startup in an environment with no user-secrets set
        // (e.g. CI). Doesn't, by itself, make token validation actually use
        // this key — see the PostConfigure<JwtBearerOptions> below for why.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:SigningKey"] = JwtSigningKey
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<DevFlowDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<DevFlowDbContext>(options => options.UseSqlite(_connection));

            // AddApiServices reads Jwt:SigningKey once, eagerly, inside the
            // AddJwtBearer(options => ...) delegate — that runs as part of
            // Program.cs's own top-level code, which (via the same
            // HostFactoryResolver/HostAbortedException mechanism used by EF's
            // design-time tooling) executes *before* WebApplicationFactory's
            // ConfigureWebHost callbacks ever get a chance to run. So the
            // ConfigureAppConfiguration override above arrives too late to
            // affect the already-captured signing key. PostConfigure runs
            // after all other configuration of an options type regardless of
            // when that configuration happened, which is exactly what's
            // needed to actually override it here.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = JwtIssuer;
                options.TokenValidationParameters.ValidAudience = JwtAudience;
                options.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey));
            });
        });
    }

    public async Task<DevFlowDbContext> CreateDbContextAsync()
    {
        var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DevFlowDbContext>();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
