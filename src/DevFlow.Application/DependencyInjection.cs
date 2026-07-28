using System.Reflection;
using DevFlow.Application.Activities;
using DevFlow.Application.Notifications;
using DevFlow.Application.Projects;
using DevFlow.Application.Tasks;
using DevFlow.Application.Team;
using FluentValidation;
using MediatR;
using MediatR.NotificationPublishers;
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
        //
        // TaskWhenAllPublisher rather than the default ForeachAwaitPublisher:
        // a single notification (e.g. TaskCreatedNotification) now fans out to
        // *independent* handlers in two different assemblies — a SignalR
        // broadcast (DevFlow.Api/Realtime) and an ActivityLog write (this
        // assembly). With the sequential default, one handler throwing would
        // silently skip every handler registered after it for that same
        // notification (e.g. a transient SignalR failure would mean the
        // activity never gets logged, even though the write itself
        // succeeded). TaskWhenAllPublisher runs all handlers regardless of
        // whether a sibling failed.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.NotificationPublisher = new TaskWhenAllPublisher();
        });

        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<IActivityLogService, ActivityLogService>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }
}
