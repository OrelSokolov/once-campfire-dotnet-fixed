namespace Campfire.Web.Cable;

/// <summary>One <c>stream_from</c>: the hub subscription once it's active.</summary>
internal sealed class CableStream( string broadcasting )
{
    private readonly Lock _gate = new( );
    private IDisposable? _subscription;
    private bool _stopped;

    public string Broadcasting { get; } = broadcasting;

    /// <summary>Subscribes to the hub unless the stream was stopped before it got going.</summary>
    public void Activate( Func<IDisposable> subscribe )
    {
        lock ( _gate )
        {
            if ( !_stopped && _subscription is null )
            {
                _subscription = subscribe( );
            }
        }
    }

    public void Stop( )
    {
        lock ( _gate )
        {
            _stopped = true;
            _subscription?.Dispose( );
            _subscription = null;
        }
    }
}
