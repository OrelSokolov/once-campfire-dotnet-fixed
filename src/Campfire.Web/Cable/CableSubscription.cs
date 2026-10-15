using System.Text.Json.Nodes;
using Campfire.Application.Serialization;

namespace Campfire.Web.Cable;

/// <summary>
/// The per-subscription state a channel works with: its identifier and params, the connection's
/// identity, its streams, rejection and transmissions (<c>Channel::Base</c> and <c>Channel::Streams</c>).
/// </summary>
public sealed class CableSubscription
{
    private readonly CableConnection _connection;
    private readonly List<CableStream> _streams = [];

    internal CableSubscription(
        CableConnection connection,
        CableChannel channel,
        string channelClassName,
        string identifier,
        IReadOnlyDictionary<string, object?> parameters )
    {
        _connection = connection;
        Channel = channel;
        ChannelClassName = channelClassName;
        Identifier = identifier;
        EncodedIdentifier = CableProtocol.EncodeIdentifier( identifier );
        Params = parameters;
        channel.Attach( this );
    }

    /// <summary>The identifier string exactly as the client sent it; frames echo it byte for byte.</summary>
    public string Identifier { get; }

    /// <summary>The decoded identifier (<c>params</c>), <c>channel</c> included.</summary>
    public IReadOnlyDictionary<string, object?> Params { get; }

    /// <summary>The Ruby class name the channel is registered under (the identifier's <c>channel</c>, without a leading <c>::</c>).</summary>
    public string ChannelClassName { get; }

    public CableIdentity Identity => _connection.Identity;

    /// <summary>The server, for broadcasting to other streams.</summary>
    public CableServer Server => _connection.Server;

    /// <summary><c>subscription_rejected?</c>.</summary>
    public bool IsRejected { get; private set; }

    /// <summary>Set once <c>unsubscribed</c> starts; streams can't be added after that.</summary>
    public bool IsUnsubscribed { get; private set; }

    internal CableChannel Channel { get; }

    internal string EncodedIdentifier { get; }

    /// <summary>
    /// <c>stream_from</c>. A stream started inside a callback starts delivering once that callback's
    /// own frames (its transmissions and the confirmation) are queued ahead of it.
    /// </summary>
    public void StreamFrom( string broadcasting )
    {
        ArgumentNullException.ThrowIfNull( broadcasting );

        if ( IsUnsubscribed )
        {
            return;
        }

        var stream = new CableStream( broadcasting );
        _streams.Add( stream );
        _connection.StartStream( this, stream );
    }

    /// <summary><c>stream_for</c>: <see cref="StreamFrom"/> of <see cref="BroadcastingFor"/>.</summary>
    public void StreamFor( params IEnumerable<string> broadcastables ) => StreamFrom( BroadcastingFor( broadcastables ) );

    /// <summary><c>stop_all_streams</c>.</summary>
    public void StopAllStreams( )
    {
        foreach ( var stream in _streams )
        {
            stream.Stop( );
        }

        _streams.Clear( );
    }

    /// <summary><c>reject</c>.</summary>
    public void Reject( ) => IsRejected = true;

    /// <summary><c>transmit</c>: sends <c>{"identifier":…,"message":…}</c> to this subscriber only.</summary>
    public void Transmit( JsonNode? message ) =>
        _connection.Enqueue( CableProtocol.Message( EncodedIdentifier, JsonPayload.Encode( message ) ) );

    /// <summary><c>broadcasting_for</c> for this channel's class.</summary>
    public string BroadcastingFor( params IEnumerable<string> broadcastables ) =>
        CableNaming.BroadcastingFor( ChannelClassName, broadcastables );

    /// <summary><c>broadcast_to</c> for this channel's class.</summary>
    public int BroadcastTo( IEnumerable<string> broadcastables, JsonNode? message ) =>
        Server.Broadcast( BroadcastingFor( broadcastables ), message );

    internal void MarkUnsubscribed( ) => IsUnsubscribed = true;
}
