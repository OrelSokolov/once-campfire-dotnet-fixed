using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Hosting;

/// <summary>Bounds HTTP reads and responses until an accepted WebSocket uses cable's own deadlines.</summary>
internal sealed class FrontRequestDeadline : IDisposable
{
    private readonly HttpContext _context;
    private readonly Stream _originalBody;
    private readonly FrontRequestWatchdog _watchdog;
    private readonly Lock _gate = new( );
    private readonly IHttpWebSocketFeature? _originalWebSocket;
    private long _writeAt;
    private long _readAt;
    private bool _disposed;

    public FrontRequestDeadline( HttpContext httpContext, FrontServerOptions options, FrontRequestWatchdog watchdog )
    {
        _context = httpContext;
        _originalBody = httpContext.Request.Body;
        _watchdog = watchdog;
        if ( httpContext.Features.Get<IHttpWebSocketFeature>( ) is { IsWebSocketRequest: true } webSocket )
        {
            _originalWebSocket = webSocket;
            httpContext.Features.Set<IHttpWebSocketFeature>( new DeadlineWebSocketFeature( webSocket, this ) );
        }

        var now = Environment.TickCount64;
        _writeAt = options.WriteTimeout > TimeSpan.Zero ? now + ( long ) options.WriteTimeout.TotalMilliseconds : 0;
        if ( options.ReadTimeout > TimeSpan.Zero && (httpContext.Features.Get<IHttpRequestBodyDetectionFeature>( )?.CanHaveBody == true
            || httpContext.Request.ContentLength > 0 || httpContext.Request.Headers.TransferEncoding.Count != 0) )
        {
            _readAt = now + ( long ) options.ReadTimeout.TotalMilliseconds;
            httpContext.Request.Body = new DeadlineBody( _originalBody, this, httpContext.Request.ContentLength );
        }

        if ( _writeAt != 0 || _readAt != 0 )
        {
            watchdog.Add( this );
        }
    }

    internal void Check( long now )
    {
        lock ( _gate )
        {
            if ( !_disposed && (HasPassed( _writeAt, now ) || HasPassed( _readAt, now )) )
            {
                _readAt = 0;
                _writeAt = 0;
                _watchdog.Remove( this );
                // Kestrel's original RequestAborted token cancels outstanding body/response I/O.
                // Serialize with disposal so a late tick cannot abort a reused HttpContext.
                _context.Abort( );
            }
        }
    }

    /// <summary>A deadline (0 when none is set) at or before <paramref name="now"/>.</summary>
    private static bool HasPassed( long deadline, long now ) => deadline != 0 && now >= deadline;

    private void BodyComplete( )
    {
        lock ( _gate )
        {
            _readAt = 0;
        }
    }

    private void Upgraded( )
    {
        lock ( _gate )
        {
            _readAt = 0;
            _writeAt = 0;
        }

        _watchdog.Remove( this );
    }

    public void Dispose( )
    {
        lock ( _gate )
        {
            _disposed = true;
            _context.Request.Body = _originalBody;
            if ( _originalWebSocket is not null )
            {
                _context.Features.Set( _originalWebSocket );
            }
        }

        _watchdog.Remove( this );
    }

    private sealed class DeadlineWebSocketFeature( IHttpWebSocketFeature inner, FrontRequestDeadline deadline ) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => inner.IsWebSocketRequest;

        public async Task<WebSocket> AcceptAsync( WebSocketAcceptContext context )
        {
            var socket = await inner.AcceptAsync( context );
            deadline.Upgraded( );
            return socket;
        }
    }

    private sealed class DeadlineBody( Stream inner, FrontRequestDeadline deadline, long? length ) : Stream
    {
        private long? _remaining = length;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException( );
        public override long Position
        {
            get => throw new NotSupportedException( );
            set => throw new NotSupportedException( );
        }

        public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        {
            var read = await inner.ReadAsync( buffer, cancellationToken );
            if ( read > 0 )
            {
                _remaining -= read;
            }

            if ( buffer.Length > 0 && (read == 0 || _remaining == 0) )
            {
                deadline.BodyComplete( );
            }

            return read;
        }

        public override Task<int> ReadAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken ) =>
            ReadAsync( buffer.AsMemory( offset, count ), cancellationToken ).AsTask( );

        public override int Read( byte[] buffer, int offset, int count ) => throw new InvalidOperationException( "Use asynchronous request reads." );
        public override void Flush( ) => throw new NotSupportedException( );
        public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException( );
        public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
        public override void SetLength( long value ) => throw new NotSupportedException( );
    }
}
