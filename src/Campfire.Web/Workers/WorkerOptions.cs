namespace Campfire.Web.Workers;

/// <summary>Worker settings shared by every job kind.</summary>
public sealed class WorkerOptions
{
    /// <summary>Configuration section: <c>Campfire:Workers</c>.</summary>
    public const string SectionName = "Campfire:Workers";

    /// <summary>
    /// How long queued jobs may keep draining once shutdown begins. It must fit in what
    /// <see cref="HostOptions.ShutdownTimeout"/> (30 s) leaves after the web server stops.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds( 20 );

    /// <summary>
    /// How long the Web Push pool may keep delivering, and then destroying invalid subscriptions, once the job workers
    /// have stopped (Rails: <c>wait_for_termination(1)</c>).
    /// </summary>
    public TimeSpan PushDrainTimeout { get; set; } = TimeSpan.FromSeconds( 2 );
}
