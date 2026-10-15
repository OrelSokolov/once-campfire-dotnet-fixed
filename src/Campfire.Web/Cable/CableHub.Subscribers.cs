using System.Text;

namespace Campfire.Web.Cable;

public sealed partial class CableHub
{
    /// <summary>
    /// The subscribers of one broadcasting that receive identical deliveries: the wire-frame subscribers of one encoded
    /// identifier, or the raw subscribers (no identifier).
    /// </summary>
    private sealed class Group( string? encodedIdentifier )
    {
        private readonly Lock _gate = new( );
        private readonly LinkedList<Subscriber> _members = new( );
        private volatile Subscriber[]? _snapshot;

        public string? EncodedIdentifier { get; } = encodedIdentifier;

        /// <summary>The identifier as UTF-8 for the group's message frames; <c>null</c> for raw subscribers.</summary>
        public byte[]? IdentifierBytes { get; } = encodedIdentifier is null ? null : Encoding.UTF8.GetBytes( encodedIdentifier );

        public Subscriber[] Subscribers
        {
            get
            {
                if ( _snapshot is { } snapshot )
                {
                    return snapshot;
                }

                lock ( _gate )
                {
                    return _snapshot ??= [.. _members];
                }
            }
        }

        public void Add( Subscriber subscriber )
        {
            lock ( _gate )
            {
                subscriber.Node = _members.AddLast( subscriber );
                _snapshot = null;
            }
        }

        public int Remove( Subscriber subscriber )
        {
            lock ( _gate )
            {
                _members.Remove( subscriber.Node! );
                subscriber.Node = null;
                _snapshot = null;
                return _members.Count;
            }
        }
    }

    /// <summary>
    /// One subscription: a raw subscriber has a <paramref name="payloadCallback"/>, a wire-frame subscriber an
    /// <paramref name="encodedIdentifier"/> and a <paramref name="frameCallback"/>. Nothing is delivered once disposed.
    /// </summary>
    private sealed class Subscriber(
        CableHub hub,
        string broadcasting,
        string? encodedIdentifier,
        Action<string>? payloadCallback,
        Action<CableFrame>? frameCallback ) : IDisposable
    {
        private int _disposed;

        public string Broadcasting { get; } = broadcasting;

        public string? EncodedIdentifier { get; } = encodedIdentifier;

        public LinkedListNode<Subscriber>? Node { get; set; }

        private bool IsDisposed => Volatile.Read( ref _disposed ) != 0;

        public void DeliverPayload( string payload )
        {
            if ( !IsDisposed )
            {
                payloadCallback!( payload );
            }
        }

        public void DeliverFrame( CableFrame frame )
        {
            if ( !IsDisposed )
            {
                frameCallback!( frame );
            }
        }

        public void Dispose( )
        {
            if ( Interlocked.Exchange( ref _disposed, 1 ) == 0 )
            {
                hub.Release( this );
            }
        }
    }
}
