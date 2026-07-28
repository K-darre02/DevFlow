using System.Reflection;
using DevFlow.Application.Projects;
using DevFlow.Application.Tasks;
using DevFlow.Application.Team;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace DevFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // MediatR is used narrowly here for post-commit domain-event fan-out
        // (activity log, notifications, real-time broadcast) rather than as the
        // primary request-handling pattern — see docs/devflow/05-technical-decisions.md ADR 1.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITeamService, TeamService>();

        return services;
    }
}
