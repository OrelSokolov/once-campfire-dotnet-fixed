using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Network;

/// <summary>
/// A connection whose every read waits at most <c>readTimeout</c>, as <c>Net::HTTP</c>'s <c>read_timeout</c> bounds
/// each read of the socket (the response head, each body chunk, and TLS records alike). A read that runs out throws
/// <see cref="ReadTimeoutException"/>, which <see cref="NetHttpException.From"/> recognizes however the handler wraps it.
/// The outbound clients only ever read asynchronously.
/// </summary>
internal sealed class ReadTimeoutStream( Stream inner, TimeSpan readTimeout ) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException( "A network stream has no length." );

    public override long Position
    {
        get => throw new NotSupportedException( "A network stream has no position." );
        set => throw new NotSupportedException( "A network stream has no position." );
    }

    public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeout.CancelAfter( readTimeout );
        try
        {
            return await inner.ReadAsync( buffer, timeout.Token );
        }
        catch ( OperationCanceledException error ) when ( timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested )
        {
            throw new ReadTimeoutException( RubyErrorClasses.ReadTimeout, error );
        }
    }

    public override Task<int> ReadAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken ) =>
        ReadAsync( buffer.AsMemory( offset, count ), cancellationToken ).AsTask( );

    public override ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default ) =>
        inner.WriteAsync( buffer, cancellationToken );

    public override Task WriteAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken ) =>
        inner.WriteAsync( buffer, offset, count, cancellationToken );

    public override Task FlushAsync( CancellationToken cancellationToken ) => inner.FlushAsync( cancellationToken );

    public override void Flush( ) => inner.Flush( );

    public override int Read( byte[] buffer, int offset, int count ) =>
        throw new NotSupportedException( "Outbound requests read asynchronously, so the read timeout applies." );

    public override void Write( byte[] buffer, int offset, int count ) => inner.Write( buffer, offset, count );

    public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( "A network stream can't seek." );

    public override void SetLength( long value ) => throw new NotSupportedException( "A network stream has no length." );

    protected override void Dispose( bool disposing )
    {
        if ( disposing )
        {
            inner.Dispose( );
        }

        base.Dispose( disposing );
    }

    public override async ValueTask DisposeAsync( )
    {
        await inner.DisposeAsync( );
        await base.DisposeAsync( );
    }
}
