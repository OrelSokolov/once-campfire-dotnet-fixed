using System.Text.Json.Nodes;

namespace Campfire.Web.Cable;

/// <summary>
/// turbo-rails' <c>broadcast_stream_to</c> (<c>Turbo::Streams::Broadcasts</c>, <c>30cd8fc</c>), which every
/// <c>broadcast_*_to</c> helper ends in: the <c>&lt;turbo-stream&gt;</c> tag, already rendered (the views build it with
/// <c>TurboHelper.TurboStreamActionTag</c>), sent as a JSON string to the stream named by the streamables. Streamables
/// are the stream name's parts, already converted (GID params and symbols). Ported from Rust
/// <c>crates/cable/src/turbo.rs</c>.
/// </summary>
public static class TurboStreams
{
    /// <summary>
    /// <c>broadcast_stream_to(*streamables, content:)</c>: blank streamables are dropped, and nothing
    /// is sent if none remain. Returns how many subscriptions received it.
    /// </summary>
    public static int BroadcastStreamTo( this CableServer server, IEnumerable<string> streamables, string content )
    {
        ArgumentNullException.ThrowIfNull( server );
        ArgumentNullException.ThrowIfNull( streamables );

        var present = streamables.Where( streamable => !string.IsNullOrWhiteSpace( streamable ) ).ToList( );
        if ( present.Count == 0 )
        {
            return 0;
        }

        return server.Broadcast( CableNaming.StreamNameFrom( present ), JsonValue.Create( content ) );
    }

    /// <summary><see cref="BroadcastStreamTo"/> of rendered UTF-8 markup, JSON-escaped without a UTF-16 copy.</summary>
    internal static int BroadcastStreamToUtf8( this CableServer server, IEnumerable<string> streamables, ReadOnlySpan<byte> content )
    {
        var present = streamables.Where( streamable => !string.IsNullOrWhiteSpace( streamable ) ).ToList( );
        if ( present.Count == 0 )
        {
            return 0;
        }

        return server.BroadcastHtmlUtf8( CableNaming.StreamNameFrom( present ), content );
    }
}
