using System.Text;
using System.Text.Json.Serialization;
using DevFlow.Api.Middleware;
using DevFlow.Api.Services;
using DevFlow.Application.Common;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace DevFlow.Api;

public static class DependencyInjection
{
    // Only ever applied in Development (see Program.cs) — production serves
    // the SPA same-origin via a linked backend (ADR 6), so no CORS policy is
    // needed there at all. Locally there are two possible dev flows: the
    // Vite dev server (proxies /api/*, so the browser never sees a
    // cross-origin request there either) and running the built SPA through
    // DevFlow.Web directly, which *is* cross-origin from DevFlow.Api's
    // default port — this policy covers that second flow.
    public const string DevFrontendCorsPolicy = "DevFrontend";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(DevFrontendCorsPolicy, policy => policy
                .WithOrigins("http://localhost:5173", "http://localhost:5284", "https://localhost:7061")
                .AllowAnyHeader()
                .AllowAnyMethod());
        });

        services.AddControllers()
            // Status/Priority (and any future enum) serialize as their names
            // ("InProgress", "High") rather than raw integers — matches
            // docs/devflow/03-api-design.md's documented wire format.
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT access token from /api/auth/login or /api/auth/register."
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        services.AddHealthChecks()
            .AddDbContextCheck<DevFlowDbContext>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<JwtTokenService>();

        AddJwtAuthentication(services, configuration);

        return services;
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");

        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via `dotnet user-secrets set \"Jwt:SigningKey\" \"<value>\" --project src/DevFlow.Api`.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep claim types exactly as issued ("sub", "tenant_id", ...) rather than
                // letting the handler remap short names to long .NET claim-type URIs — this
                // is what ICurrentUserService reads by the same literal names.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSection["Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization(options =>
        {
            // Both check the JWT's "role" claim (JwtTokenService/CurrentUserService).
            // Domain-level rules that depend on the specific member being acted
            // on (e.g. "an Admin can't remove an Owner") aren't expressible as a
            // static policy — those live in TeamService, via ForbiddenException.
            options.AddPolicy(AuthorizationPolicies.OwnerOnly, policy =>
                policy.RequireClaim("role", nameof(TenantRole.Owner)));

            options.AddPolicy(AuthorizationPolicies.AdminOrOwner, policy =>
                policy.RequireClaim("role", nameof(TenantRole.Owner), nameof(TenantRole.Admin)));
        });
    }
}
