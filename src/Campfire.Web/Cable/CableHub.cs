using System.Text;
using Campfire.Application.Serialization;

namespace Campfire.Web.Cable;

/// <summary>
/// The in-process replacement for Rails' Redis subscription adapter. Payloads are already-encoded
/// JSON, as they are on the Redis wire. Wire-frame subscribers that wrap a payload identically (the
/// same encoded channel identifier) form one group, and each broadcast builds their
/// <c>{"identifier":…,"message":…}</c> frame once for the whole group; raw subscribers (a
/// connection's internal channel) get the payload itself. Delivery hands frames to each
/// connection's bounded outbox without waiting, so a broadcast never blocks on a slow socket.
/// Ported from Rust <c>crates/cable/src/pubsub.rs</c>.
/// </summary>
public sealed partial class CableHub
{
    private readonly Lock _gate = new( );

    // Group membership changes invalidate a lazy snapshot; delivery always happens outside locks.
    private readonly Dictionary<string, Group[]> _streams = new( StringComparer.Ordinal );

    /// <summary>Broadcastings with at least one subscriber (connections' internal channels included).</summary>
    public int StreamCount
    {
        get
        {
            lock ( _gate )
            {
                return _streams.Count;
            }
        }
    }

    /// <summary>Publishes to every current subscriber of <paramref name="broadcasting"/>; returns how many received it.</summary>
    public int Broadcast( string broadcasting, string payload )
    {
        ArgumentNullException.ThrowIfNull( broadcasting );
        ArgumentNullException.ThrowIfNull( payload );

        if ( Snapshot( broadcasting ) is not { } groups )
        {
            return 0;
        }

        return Deliver( groups, payload, payloadUtf8: null );
    }

    /// <summary>
    /// Takes the stream snapshot before JSON escaping rendered markup. An absent stream still validates UTF-8,
    /// so malformed input does not become silently accepted merely because its last subscriber disconnected.
    /// </summary>
    internal int BroadcastHtmlUtf8( string broadcasting, ReadOnlySpan<byte> html )
    {
        ArgumentNullException.ThrowIfNull( broadcasting );

        if ( Snapshot( broadcasting ) is not { } groups )
        {
            JsonUtf8.ValidateString( html );
            return 0;
        }

        return Deliver( groups, payload: null, JsonUtf8.EncodeString( html ) );
    }

    /// <summary>
    /// Subscribes to <paramref name="broadcasting"/>: <paramref name="deliver"/> receives each raw payload. Dispose the
    /// result to unsubscribe.
    /// </summary>
    public IDisposable Subscribe( string broadcasting, Action<string> deliver )
    {
        ArgumentNullException.ThrowIfNull( broadcasting );
        ArgumentNullException.ThrowIfNull( deliver );

        return AddSubscriber( new Subscriber( this, broadcasting, encodedIdentifier: null, deliver, frameCallback: null ) );
    }

    /// <summary>
    /// A transport subscription: <paramref name="deliver"/> receives each payload wrapped as the subscription's message
    /// frame, one lazy immutable wire-frame holder shared by its identifier group. Dispose the result to unsubscribe.
    /// </summary>
    internal IDisposable SubscribeWireFrames( string broadcasting, string encodedIdentifier, Action<CableFrame> deliver )
    {
        ArgumentNullException.ThrowIfNull( broadcasting );
        ArgumentNullException.ThrowIfNull( encodedIdentifier );
        ArgumentNullException.ThrowIfNull( deliver );

        return AddSubscriber( new Subscriber( this, broadcasting, encodedIdentifier, payloadCallback: null, deliver ) );
    }

    /// <summary>How many groups (distinct frames per broadcast) a broadcasting has; for tests.</summary>
    internal int GroupCount( string broadcasting ) => Snapshot( broadcasting )?.Length ?? 0;

    /// <summary>
    /// Hands one broadcast to every group: raw subscribers get the payload as a string, the others one frame shared by
    /// their group. Whichever of <paramref name="payload"/> and <paramref name="payloadUtf8"/> is missing is made once,
    /// when first needed.
    /// </summary>
    private static int Deliver( Group[] groups, string? payload, byte[]? payloadUtf8 )
    {
        var receivers = 0;
        foreach ( var group in groups )
        {
            var subscribers = group.Subscribers;
            if ( group.IdentifierBytes is { } identifier )
            {
                payloadUtf8 ??= Encoding.UTF8.GetBytes( payload! );
                var frame = new CableFrame( MessageFrame( identifier, payloadUtf8 ) );
                foreach ( var subscriber in subscribers )
                {
                    subscriber.DeliverFrame( frame );
                }
            }
            else
            {
                payload ??= Encoding.UTF8.GetString( payloadUtf8! );
                foreach ( var subscriber in subscribers )
                {
                    subscriber.DeliverPayload( payload );
                }
            }

            receivers += subscribers.Length;
        }

        return receivers;
    }

    /// <summary><see cref="CableProtocol.Message"/> as UTF-8, from the encoded identifier and message.</summary>
    private static byte[] MessageFrame( ReadOnlySpan<byte> identifier, ReadOnlySpan<byte> payload )
    {
        var prefix = "{\"identifier\":"u8;
        var middle = ",\"message\":"u8;
        var frame = new byte[prefix.Length + identifier.Length + middle.Length + payload.Length + 1];
        prefix.CopyTo( frame );
        identifier.CopyTo( frame.AsSpan( prefix.Length ) );
        middle.CopyTo( frame.AsSpan( prefix.Length + identifier.Length ) );
        payload.CopyTo( frame.AsSpan( prefix.Length + identifier.Length + middle.Length ) );
        frame[^1] = ( byte ) '}';
        return frame;
    }

    /// <summary>The groups subscribed to <paramref name="broadcasting"/> right now, or <c>null</c> for none.</summary>
    private Group[]? Snapshot( string broadcasting )
    {
        lock ( _gate )
        {
            _streams.TryGetValue( broadcasting, out var groups );
            return groups;
        }
    }

    private Subscriber AddSubscriber( Subscriber subscriber )
    {
        var broadcasting = subscriber.Broadcasting;
        lock ( _gate )
        {
            var groups = _streams.GetValueOrDefault( broadcasting ) ?? [];
            var group = FindGroup( groups, subscriber.EncodedIdentifier );
            if ( group is null )
            {
                group = new Group( subscriber.EncodedIdentifier );
                _streams[broadcasting] = [.. groups, group];
            }

            group.Add( subscriber );
        }

        return subscriber;
    }

    private void Release( Subscriber subscriber )
    {
        lock ( _gate )
        {
            if ( !_streams.TryGetValue( subscriber.Broadcasting, out var groups ) )
            {
                return;
            }

            var group = FindGroup( groups, subscriber.EncodedIdentifier );
            if ( group is null || group.Remove( subscriber ) > 0 )
            {
                return;
            }

            var remaining = Array.FindAll( groups, existing => !ReferenceEquals( existing, group ) );
            if ( remaining.Length == 0 )
            {
                _streams.Remove( subscriber.Broadcasting );
            }
            else
            {
                _streams[subscriber.Broadcasting] = remaining;
            }
        }
    }

    private static Group? FindGroup( Group[] groups, string? encodedIdentifier )
    {
        foreach ( var group in groups )
        {
            if ( group.EncodedIdentifier == encodedIdentifier )
            {
                return group;
            }
        }

        return null;
    }
}
