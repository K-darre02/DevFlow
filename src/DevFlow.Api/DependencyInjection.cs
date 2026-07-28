using DevFlow.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DevFlow.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddHealthChecks()
            .AddDbContextCheck<DevFlowDbContext>();

        return services;
    }
}
