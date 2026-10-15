namespace Campfire.IntegrationTests.Support;

/// <summary>Faults a test arms before a request: each runs once, at its hook.</summary>
public sealed class SliceFaults
{
    /// <summary>Runs inside the after-commit upload step, before the file is placed.</summary>
    public Action? DuringUpload { get; set; }

    /// <summary>
    /// Runs after a remote disconnect (user id, reconnect). Deactivation and bans invoke this after commit.
    /// </summary>
    public Action<long, bool>? AfterDisconnect { get; set; }
}
