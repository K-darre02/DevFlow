using DevFlow.Api;
using DevFlow.Api.Realtime;
using DevFlow.Application;
using DevFlow.Infrastructure;
using Microsoft.Extensions.Hosting;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.Console());

    // Each layer owns its own DI registration; Program.cs only composes them.
    // See docs/devflow/01-architecture.md §3 for the layering this mirrors.
    builder.Services
        .AddApplicationServices()
        .AddInfrastructureServices(builder.Configuration)
        .AddApiServices(builder.Configuration);

    var app = builder.Build();

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        // Dev-only — see the DevFrontendCorsPolicy comment in DependencyInjection.cs.
        // Fully qualified: DevFlow.Application also has a DependencyInjection class,
        // and both namespaces are already in scope via the usings above.
        app.UseCors(DevFlow.Api.DependencyInjection.DevFrontendCorsPolicy);
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");
    app.MapHub<TaskHub>("/hubs/tasks");

    app.Run();
}
catch (HostAbortedException)
{
    // Thrown by EF Core design-time tooling (e.g. `dotnet ef migrations add`),
    // which builds the host far enough to extract the DbContext, then aborts
    // it deliberately without running the app. Not a real failure.
}
catch (Exception ex)
{
    Log.Fatal(ex, "DevFlow.Api terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the implicit Program class for WebApplicationFactory<Program> in DevFlow.IntegrationTests.
public partial class Program
{
}
