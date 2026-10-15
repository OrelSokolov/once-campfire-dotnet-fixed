using System.Text.Json.Nodes;
using Campfire.Application.Serialization;

namespace Campfire.Web.Cable;

/// <summary>
/// The Action Cable wire format (<c>ActionCable::INTERNAL</c> in actioncable/lib/action_cable.rb at
/// rails <c>1a02651</c>). Frames are Ruby hashes run through <c>ActiveSupport::JSON.encode</c>, so
/// key order is the order the hash literal is written in <c>Connection::Base</c> and
/// <c>Channel::Base</c>, and <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> are escaped. Ported from Rust
/// <c>crates/cable/src/protocol.rs</c>.
/// </summary>
public static class CableProtocol
{
    /// <summary><c>ActionCable::INTERNAL[:default_mount_path]</c>.</summary>
    public const string DefaultMountPath = "/cable";
    public const string WebSocketProtocol = "websocket";
    internal const string UpgradeToken = "upgrade";

    /// <summary>The protocol the JavaScript client speaks.</summary>
    public const string JsonProtocol = "actioncable-v1-json";

    /// <summary>Offered so an old client learns the server can't speak its protocol.</summary>
    public const string UnsupportedProtocol = "actioncable-unsupported";

    /// <summary>
    /// <c>ActionCable::INTERNAL[:protocols]</c>. The first protocol the client lists that is in here
    /// wins (websocket-driver walks the client's list, not the server's).
    /// </summary>
    public static IReadOnlyList<string> Protocols { get; } = [JsonProtocol, UnsupportedProtocol];

    /// <summary><c>ActionCable::Server::Connections::BEAT_INTERVAL</c>.</summary>
    public static TimeSpan BeatInterval { get; } = TimeSpan.FromSeconds( 3 );

    /// <summary><c>{"type":"welcome"}</c>.</summary>
    public static string Welcome( ) => "{\"type\":\"welcome\"}";

    /// <summary><c>{"type":"ping","message":&lt;unix seconds&gt;}</c> (<c>Connection::Base#beat</c>).</summary>
    public static string Ping( long unixSeconds ) => $"{{\"type\":\"ping\",\"message\":{unixSeconds}}}";

    /// <summary>
    /// <c>{"type":"disconnect","reason":…,"reconnect":…}</c> (<c>Connection::Base#close</c>).
    /// <paramref name="reason"/> is <c>null</c> when Rails closes without one; <paramref name="reconnect"/>
    /// is whatever JSON value a remote disconnect carried (<c>message.fetch("reconnect", true)</c>).
    /// </summary>
    public static string Disconnect( string? reason, JsonNode? reconnect )
    {
        using var writer = new JsonPayloadWriter( );
        writer.WriteStartObject( );
        writer.WritePropertyName( CableMessageKeys.Type );
        writer.WriteString( CableMessageTypes.Disconnect );
        writer.WritePropertyName( CableMessageKeys.Reason );
        writer.WriteString( reason );
        writer.WritePropertyName( CableMessageKeys.Reconnect );
        writer.WriteNode( reconnect );
        writer.WriteEndObject( );

        return writer.ToString( );
    }

    /// <summary><c>{"identifier":…,"type":"confirm_subscription"}</c>.</summary>
    public static string Confirmation( string identifier ) => $"{{\"identifier\":{EncodeIdentifier( identifier )},\"type\":\"confirm_subscription\"}}";

    /// <summary><c>{"identifier":…,"type":"reject_subscription"}</c>.</summary>
    public static string Rejection( string identifier ) => $"{{\"identifier\":{EncodeIdentifier( identifier )},\"type\":\"reject_subscription\"}}";

    /// <summary>
    /// <c>{"identifier":…,"message":…}</c> from an encoded identifier and an already-encoded message.
    /// Rails decodes each broadcast and re-encodes it inside this hash; ActiveSupport's encoding is
    /// stable under that round trip, so passing the encoded payload through is equivalent.
    /// </summary>
    public static string Message( string encodedIdentifier, string encodedMessage ) =>
        string.Concat( "{\"identifier\":", encodedIdentifier, ",\"message\":", encodedMessage, "}" );

    /// <summary>The identifier string as a JSON string literal, escaped as ActiveSupport does.</summary>
    public static string EncodeIdentifier( string identifier ) => JsonPayload.EncodeString( identifier );
}
