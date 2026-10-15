using System.Buffers;
using System.IO.Pipelines;
using Campfire.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Tests.Views;

public sealed class CachedBodyWriteTests
{
    [Theory]
    [InlineData( 0 )]
    [InlineData( 2048 )]
    [InlineData( 65536 )]
    public async Task Small_owned_bodies_use_one_write_without_an_extra_flush_and_obey_backpressure( int length )
    {
        var input = Enumerable.Range( 0, length ).Select( index => ( byte ) (index % 251) ).ToArray( );
        var expected = input.ToArray( );
        var body = SegmentedPageBody.Copy( ResponseBody.FromBytes( input ) )!;
        Array.Clear( input );
        var pipe = new Pipe( new PipeOptions( pauseWriterThreshold: 64, resumeWriterThreshold: 32 ) );
        var writer = new ObservedWriter( pipe.Writer );
        var httpContext = new DefaultHttpContext( );
        httpContext.Features.Set<IHttpResponseBodyFeature>( new BodyFeature( writer ) );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        var writing = body.WriteAsync( httpContext.Response, timeout.Token );
        if ( length > 0 )
        {
            var read = await pipe.Reader.ReadAsync( timeout.Token );
            Assert.False( writing.IsCompleted );
            Assert.Equal( expected, read.Buffer.ToArray( ) );
            pipe.Reader.AdvanceTo( read.Buffer.End );
        }

        await writing;
        Assert.Equal( 1, writer.Writes );
        Assert.Equal( 0, writer.Flushes );
        await pipe.Reader.CompleteAsync( );
        await pipe.Writer.CompleteAsync( );
    }

    [Fact]
    public async Task A_cancelled_slow_reader_releases_the_pending_cached_body_write( )
    {
        var body = SegmentedPageBody.Copy( ResponseBody.FromBytes( new byte[2048] ) )!;
        var pipe = new Pipe( new PipeOptions( pauseWriterThreshold: 64, resumeWriterThreshold: 32 ) );
        var httpContext = new DefaultHttpContext( );
        httpContext.Features.Set<IHttpResponseBodyFeature>( new BodyFeature( pipe.Writer ) );
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        cancellation.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        var writing = body.WriteAsync( httpContext.Response, cancellation.Token );
        var read = await pipe.Reader.ReadAsync( cancellation.Token );
        Assert.False( writing.IsCompleted );
        await cancellation.CancelAsync( );
        await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => writing );
        pipe.Reader.AdvanceTo( read.Buffer.End );
        await pipe.Reader.CompleteAsync( );
        await pipe.Writer.CompleteAsync( );
    }

    private sealed class BodyFeature( PipeWriter writer ) : IHttpResponseBodyFeature
    {
        public Stream Stream => Stream.Null;
        public PipeWriter Writer => writer;
        public void DisableBuffering( ) { }
        public Task StartAsync( CancellationToken cancellationToken = default ) => Task.CompletedTask;
        public Task CompleteAsync( ) => Task.CompletedTask;
        public Task SendFileAsync( string path, long offset, long? count, CancellationToken cancellationToken = default ) => throw new NotSupportedException( );
    }

    private sealed class ObservedWriter( PipeWriter inner ) : PipeWriter
    {
        internal int Writes { get; private set; }
        internal int Flushes { get; private set; }
        public override void Advance( int bytes ) => inner.Advance( bytes );
        public override Memory<byte> GetMemory( int sizeHint = 0 ) => inner.GetMemory( sizeHint );
        public override Span<byte> GetSpan( int sizeHint = 0 ) => inner.GetSpan( sizeHint );
        public override void CancelPendingFlush( ) => inner.CancelPendingFlush( );
        public override void Complete( Exception? exception = null ) => inner.Complete( exception );
        public override ValueTask<FlushResult> FlushAsync( CancellationToken cancellationToken = default )
        {
            Flushes++;
            return inner.FlushAsync( cancellationToken );
        }

        public override ValueTask<FlushResult> WriteAsync( ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default )
        {
            Writes++;
            return inner.WriteAsync( source, cancellationToken );
        }
    }
}
