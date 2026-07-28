var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Serves the built React SPA's static output from wwwroot/. Local/alternative
// stand-in for the documented Azure Static Web Apps deployment — see
// docs/devflow/01-architecture.md §1 and §9.
app.UseDefaultFiles();
app.UseStaticFiles();

// React Router handles routes like /login client-side. Without this, a hard
// refresh or direct link to /login would 404 here (no physical file at that
// path) instead of loading index.html and letting the client-side router
// take over.
app.MapFallbackToFile("index.html");

app.Run();
