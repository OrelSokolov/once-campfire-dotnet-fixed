namespace Campfire.Web.Cable;

/// <summary>
/// <c>config.action_cable.*</c> as the production Rails app runs it, plus the bounds Rails doesn't
/// have (see docs/compatibility.md#deliberate-differences-and-bounds).
/// </summary>
public sealed class CableOptions
{
    /// <summary>Configuration section: <c>Campfire:Cable</c>.</summary>
    public const string SectionName = "Campfire:Cable";

    private string _mountPath = CableProtocol.DefaultMountPath;
    private MountPaths? _mountPaths;

    /// <summary><c>config.action_cable.mount_path</c>.</summary>
    public string MountPath
    {
        get => _mountPath;
        set
        {
            _mountPath = value;
            _mountPaths = null;
        }
    }

    /// <summary><c>config.action_cable.disable_request_forgery_protection</c> (off in production).</summary>
    public bool DisableRequestForgeryProtection { get; set; }

    /// <summary>Negotiate shared permessage-deflate frames, without context takeover in either direction.</summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// <c>config.action_cable.allowed_request_origins</c>: exact <c>Origin</c> values accepted besides
    /// the same-origin rule (production sets none; Rails' regexp form isn't supported).
    /// </summary>
    public IList<string> AllowedRequestOrigins { get; } = [];

    /// <summary><c>config.action_cable.allow_same_origin_as_host</c>.</summary>
    public bool AllowSameOriginAsHost { get; set; } = true;

    /// <summary>
    /// <c>config.assume_ssl</c> (on unless <c>DISABLE_SSL</c> is set): <c>ActionDispatch::AssumeSSL</c>
    /// makes every request look like HTTPS, so the same-origin check compares against
    /// <c>https://&lt;host&gt;</c>.
    /// </summary>
    public bool AssumeSsl { get; set; } = true;

    /// <summary><c>BEAT_INTERVAL</c>: how often every connection is pinged.</summary>
    public TimeSpan BeatInterval { get; set; } = CableProtocol.BeatInterval;

    /// <summary>Subscriptions per connection; past it, a subscribe is ignored and logged (Rails has no limit).</summary>
    public int MaxSubscriptions { get; set; } = 64;

    /// <summary>Longest subscription identifier, in UTF-8 bytes; a longer one is ignored and logged.</summary>
    public int MaxIdentifierBytes { get; set; } = 4 * 1024;

    /// <summary>Largest incoming message; a larger one closes the socket with 1009 (message too big).</summary>
    public int MaxMessageBytes { get; set; } = 1024 * 1024;

    /// <summary>The initial read buffer per connection. Clients send small commands; it grows on demand.</summary>
    public int ReceiveBufferBytes { get; set; } = 4 * 1024;

    /// <summary>
    /// Frames queued for a connection beyond the one being written. A client that falls further
    /// behind is disconnected with <c>reconnect: true</c>, never silently skipped.
    /// </summary>
    public int MaxPendingFrames { get; set; } = 256;

    /// <summary>Bytes queued for a connection beyond the frame being written; same consequence as <see cref="MaxPendingFrames"/>.</summary>
    public long MaxPendingBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Longest a single socket write may take before the connection is aborted.</summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds( 30 );

    /// <summary>How long to wait for the client's close frame after the server closes.</summary>
    public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds( 5 );

    /// <summary>
    /// Whether a request is for the cable: <see cref="MountPath"/>, with or without a trailing <c>/</c> (as
    /// <see cref="PathString"/> compares, ignoring case). The two paths are built once, not per request.
    /// </summary>
    internal bool IsMountPath( PathString path )
    {
        var mountPaths = _mountPaths ??= new MountPaths( new PathString( _mountPath ), new PathString( _mountPath + "/" ) );
        return path == mountPaths.Exact || path == mountPaths.WithTrailingSlash;
    }

    internal IEnumerable<string> Validate( )
    {
        if ( string.IsNullOrEmpty( MountPath ) || !MountPath.StartsWith( '/' ) )
        {
            yield return "MountPath must start with '/'.";
        }

        if ( BeatInterval <= TimeSpan.Zero || WriteTimeout <= TimeSpan.Zero || CloseTimeout <= TimeSpan.Zero )
        {
            yield return "BeatInterval, WriteTimeout and CloseTimeout must be positive.";
        }

        if ( MaxSubscriptions < 1 || MaxIdentifierBytes < 1 || MaxPendingFrames < 1 || MaxPendingBytes < 1 )
        {
            yield return "Subscription, identifier and pending bounds must be positive.";
        }

        if ( ReceiveBufferBytes < 1 || MaxMessageBytes < ReceiveBufferBytes )
        {
            yield return "ReceiveBufferBytes must be positive and no larger than MaxMessageBytes.";
        }
    }

    private sealed record MountPaths( PathString Exact, PathString WithTrailingSlash );
}
