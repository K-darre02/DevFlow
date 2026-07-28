using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DevFlow.Api;
using DevFlow.Api.Realtime;
using DevFlow.Application;
using DevFlow.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Production/staging App Service configures KeyVault:Uri as a plain
    // (non-secret) app setting; everything actually sensitive — Jwt:SigningKey,
    // ConnectionStrings:DevFlowDatabase, Storage:Azure:ConnectionString —
    // lives in the vault itself and is pulled in here, before any of the
    // AddXServices calls below read configuration to register anything.
    // DefaultAzureCredential resolves to the App Service's system-assigned
    // managed identity in Azure — no client secret involved. Absent
    // locally/in tests (no KeyVault:Uri configured there), so this is a
    // pure no-op for the local Docker Compose + User Secrets workflow
    // documented in docs/devflow/01-architecture.md §9.
    var keyVaultUri = builder.Configuration["KeyVault:Uri"];
    if (!string.IsNullOrWhiteSpace(keyVaultUri))
    {
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
    }

    // Same presence-gated pattern as Key Vault above: only wired up when
    // deployed with an actual Application Insights resource behind it
    // (APPLICATIONINSIGHTS_CONNECTION_STRING, conventionally sourced from
    // the vault). UseAzureMonitor auto-instruments requests, dependencies
    // (HttpClient, EF Core/Npgsql), and exceptions; writeToProviders below
    // is what additionally routes Serilog's own structured log events
    // (correlation IDs included, via Serilog's enrichers) into the same
    // exporter rather than leaving them console-only.
    var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
    {
        builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
            options.ConnectionString = appInsightsConnectionString);
    }

    builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        // Required for the TraceIdentifier pushed by the middleware below
        // to actually attach to log events written during request handling
        // — without this, LogContext.PushProperty has nothing to enrich.
        .Enrich.FromLogContext()
        .WriteTo.Console(),
        writeToProviders: true);

    // Each layer owns its own DI registration; Program.cs only composes them.
    // See docs/devflow/01-architecture.md §3 for the layering this mirrors.
    builder.Services
        .AddApplicationServices()
        .AddInfrastructureServices(builder.Configuration)
        .AddApiServices(builder.Configuration);

    var app = builder.Build();

    app.UseExceptionHandler();

    // ASP.NET Core generates a TraceIdentifier per request regardless; this
    // pushes it into Serilog's LogContext so every log statement written
    // while handling this request — not just the one-line summary
    // UseSerilogRequestLogging emits below — carries the same value, which
    // is what actually lets a request's full log trail be reconstructed
    // (see docs/devflow/07-quality-attributes.md §4).
    app.Use(async (context, next) =>
    {
        using (Serilog.Context.LogContext.PushProperty("TraceIdentifier", context.TraceIdentifier))
        {
            await next();
        }
    });

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("TraceIdentifier", httpContext.TraceIdentifier);
            diagnosticContext.Set("TenantId", httpContext.User.FindFirst("tenant_id")?.Value);
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        // Dev-only — see the DevFrontendCorsPolicy comment in DependencyInjection.cs.
        // Fully qualified: DevFlow.Application also has a DependencyInjection class,
        // and both namespaces are already in scope via the usings above.
        app.UseCors(DevFlow.Api.DependencyInjection.DevFrontendCorsPolicy);
    }

    // Azure App Service (Linux) terminates TLS at its own front-end and
    // forwards plain HTTP to Kestrel, setting X-Forwarded-Proto/-For —
    // without honoring those, Kestrel sees every request as HTTP, and
    // UseHttpsRedirection right below would redirect-loop genuinely-HTTPS
    // client requests. A safe no-op locally, where no such proxy is in
    // front of Kestrel and these headers are never set.
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    // App Service's front-end IPs aren't a fixed, documented set the way a
    // self-managed reverse proxy's would be — only App Service's own
    // infrastructure can reach Kestrel on the internal port in this hosting
    // model, so that boundary is the trust boundary, not KnownProxies'
    // default (loopback only, which App Service's front-end isn't).
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

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
