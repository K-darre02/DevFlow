var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Serves the built React SPA's static output from wwwroot/. Local/alternative
// stand-in for the documented Azure Static Web Apps deployment — see
// docs/devflow/01-architecture.md §1 and §9.
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
