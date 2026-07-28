using DevFlow.Application.Common;
using DevFlow.Application.Search;
using DevFlow.Infrastructure.Persistence;
using DevFlow.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<DevFlowDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DevFlowDatabase")));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<DevFlowDbContext>());

        // Overrides AddApplicationServices's LikeSearchService default with
        // the real Postgres full-text search implementation, since this is
        // the DbContext registration that actually uses Npgsql — see
        // ISearchService's doc comment.
        services.AddScoped<ISearchService, PostgresFullTextSearchService>();

        return services;
    }
}
