namespace Campfire.Web.Cable;

internal sealed partial class CableConnection
{
    /// <summary>
    /// Starts a callback. Its frames are held back, and streams it starts wait until those frames are queued, so
    /// deliveries follow the confirmation. Disposing the result ends the callback: its streams go live, then its frames
    /// become sendable, so the client can't see a confirmation before the stream it confirms delivers.
    /// </summary>
    public CallbackScope EnterCallback( )
    {
        if ( _callbackDepth++ == 0 )
        {
            _outbox.Cork( );
        }

        return new CallbackScope( this );
    }

    /// <summary>
    /// <c>stream_from</c>'s subscription: at once outside a callback, otherwise once the callback ends (see
    /// <see cref="EnterCallback"/>).
    /// </summary>
    public void StartStream( CableSubscription subscription, CableStream stream )
    {
        if ( _callbackDepth > 0 )
        {
            _pendingStreams.Add( (subscription, stream) );
        }
        else
        {
            Activate( subscription, stream );
        }
    }

    private void EndCallback( )
    {
        if ( --_callbackDepth > 0 )
        {
            return;
        }

        var pending = _pendingStreams.ToList( );
        _pendingStreams.Clear( );
        foreach ( var (subscription, stream) in pending )
        {
            Activate( subscription, stream );
        }

        _outbox.Uncork( );
    }

    private void Activate( CableSubscription subscription, CableStream stream ) =>
        stream.Activate( ( ) => Server.Hub.SubscribeWireFrames( stream.Broadcasting, subscription.EncodedIdentifier, DeliverFrame ) );

    /// <summary>A callback in progress (<see cref="EnterCallback"/>); disposing it ends the callback.</summary>
    internal readonly struct CallbackScope( CableConnection connection ) : IDisposable
    {
        public void Dispose( ) => connection.EndCallback( );
    }
}
