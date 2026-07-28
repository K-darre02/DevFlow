using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DevFlow.Api.Middleware;
using DevFlow.Api.Services;
using DevFlow.Api.Storage;
using DevFlow.Application.Common;
using DevFlow.Domain.Entities;
using DevFlow.Domain.Enums;
using DevFlow.Infrastructure.Persistence;
using DevFlow.Infrastructure.Storage;
using MediatR;
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

        // Self-hosted ASP.NET Core SignalR (built into the shared framework —
        // no separate package). See docs/devflow/01-architecture.md §9: this
        // is the documented local/single-instance substitute for Azure
        // SignalR Service, used directly here rather than as a stand-in.
        // AddJsonProtocol has its own JsonSerializerOptions, entirely
        // separate from AddControllers().AddJsonOptions above — without this,
        // enums broadcast over the hub would serialize as raw integers while
        // the REST API sends their names, a silent mismatch the frontend
        // would otherwise have to work around twice.
        // AddJsonProtocol has its own JsonSerializerOptions, entirely
        // separate from AddControllers().AddJsonOptions above — without this,
        // enums broadcast over the hub would serialize as raw integers while
        // the REST API sends their names. Any .NET SignalR client must
        // configure a matching JsonStringEnumConverter of its own (there's no
        // content negotiation for payload shape the way REST has
        // Content-Type — see TaskHubTests.BuildConnection) or it will fail to
        // deserialize incoming messages silently. A browser/JS client is
        // unaffected: JSON strings map to plain JS strings either way.
        services.AddSignalR()
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // DevFlow.Application's own AddMediatR call (DevFlow.Application/
        // DependencyInjection.cs) only scans its own assembly, so it never
        // finds the INotificationHandler<T> implementations in
        // DevFlow.Api/Realtime — those live here specifically because they
        // depend on IHubContext<TaskHub>, which is an Api-layer/SignalR
        // concern Application must not reference. A second
        // RegisterServicesFromAssembly call, scoped to this assembly, is
        // what actually wires them up; MediatR's core services
        // (IMediator/ISender/IPublisher) are safe to register more than
        // once. NotificationPublisher is set identically here too — see the
        // comment on the Application-layer AddMediatR call for why.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.NotificationPublisher = new MediatR.NotificationPublishers.TaskWhenAllPublisher();
        });

        AddJwtAuthentication(services, configuration);
        AddBlobStorage(services, configuration);

        return services;
    }

    // Azure Blob Storage when Storage:Azure:ConnectionString is configured,
    // the local-filesystem substitute otherwise — see
    // docs/devflow/01-architecture.md §9 for the same local/cloud
    // substitution pattern already used for SignalR.
    private static void AddBlobStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureBlobStorageOptions>(configuration.GetSection("Storage:Azure"));
        services.Configure<LocalFileStorageOptions>(configuration.GetSection("Storage:Local"));

        // Unlike Jwt:SigningKey (which fails fast if missing — a real
        // deployment secret that must be deliberately set), local storage's
        // signing key defaults to a fresh random value per process start
        // when unconfigured: it only has to remain valid for this process's
        // own short-lived download URLs, and requiring a manually-set
        // secret just to try file uploads locally would be unnecessary
        // friction for exactly the "local development" case this
        // implementation exists for.
        services.PostConfigure<LocalFileStorageOptions>(options =>
        {
            if (string.IsNullOrEmpty(options.SigningKey))
            {
                options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            }
        });

        var azureConnectionString = configuration["Storage:Azure:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(azureConnectionString))
        {
            services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        }
        else
        {
            services.AddScoped<IBlobStorageService, LocalFileBlobStorageService>();
        }
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

                // Browsers' native WebSocket API can't set an Authorization
                // header, so SignalR's JS client sends the token as an
                // "access_token" query string parameter on the hub request
                // instead — this is the standard, documented way to bridge
                // that onto the same JWT bearer handler used everywhere else,
                // scoped to hub paths only so it doesn't relax normal REST
                // endpoints (which still require a real Authorization header).
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
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
