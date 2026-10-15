using System.Diagnostics.CodeAnalysis;
using Campfire.Application.Integrations.WebPush;

namespace Campfire.Web.Workers;

/// <summary>Registers the worker framework and job kinds.</summary>
public static class WorkerServiceCollectionExtensions
{
    /// <summary>Queued jobs per Active Job kind before the full-queue policy applies.</summary>
    public const int JobCapacity = 1_000;

    /// <summary>
    /// The worker settings, the outbound integrations the jobs use, and <see cref="JobChannels"/>. The job kinds
    /// themselves are registered one line each with <see cref="AddJobKind{TJob, THandler}"/> (<c>AppRegistrations</c>).
    /// Web Push deliveries run on the <see cref="WebPushPool"/>
    /// (<see cref="OutboundIntegrations.AddOutboundIntegrations"/>), not on a job kind.
    /// </summary>
    public static IServiceCollection AddCampfireWorkers( this IServiceCollection services )
    {
        ArgumentNullException.ThrowIfNull( services );

        services.AddOptions<WorkerOptions>( ).BindConfiguration( WorkerOptions.SectionName );

        // Registered ahead of the job workers, so the pool shuts down after they have drained into it.
        services.AddOutboundIntegrations( );
        services.AddSingleton<JobChannels>( );

        return services;
    }

    /// <summary>
    /// A job kind: its queue, its worker, and the handler that performs each job in a scope of its own. The annotation
    /// keeps the handler's public constructors, which the container looks up, under trimming and Native AOT.
    /// </summary>
    public static IServiceCollection AddJobKind<TJob, [DynamicallyAccessedMembers( DynamicallyAccessedMemberTypes.PublicConstructors )] THandler>(
        this IServiceCollection services,
        JobKind kind )
        where TJob : notnull
        where THandler : class, IJobHandler<TJob>
    {
        ArgumentNullException.ThrowIfNull( kind );

        services.AddSingleton( provider => new JobChannel<TJob>( kind, provider.GetRequiredService<ILogger<JobChannel<TJob>>>( ) ) );
        services.AddScoped<IJobHandler<TJob>, THandler>( );
        services.AddHostedService<JobWorker<TJob>>( );

        return services;
    }
}
