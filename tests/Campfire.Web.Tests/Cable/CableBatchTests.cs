using System.Net.WebSockets;
using System.Text;
using Campfire.Web.Cable;

namespace Campfire.Web.Tests.Cable;

/// <summary>Transport batching must preserve WebSocket message boundaries and bound each socket write.</summary>
public sealed class CableBatchTests
{
    [Fact]
    public async Task Batched_frames_preserve_order_across_flushes_and_large_passthrough_writes( )
    {
        using var wire = new RecordingStream( );
        using var batch = new CableBatchStream( wire );
        var expected = new List<byte>( );
        batch.BeginBatch( );
        foreach ( var size in new[] { 511, 8192, 10000, 30000, 32768, 70000, 513 } )
        {
            var part = Enumerable.Range( 0, size ).Select( index => ( byte ) (index % 251) ).ToArray( );
            expected.AddRange( part );
            await batch.WriteAsync( part, TestContext.Current.CancellationToken );
        }

        await batch.EndBatchAsync( TestContext.Current.CancellationToken );
        Assert.Equal( expected, wire.ToArray( ) );
        Assert.Equal( [8703, 10000, 30000, 32768, 70000, 513], wire.Sizes );
    }

    [Fact]
    public async Task Abort_keeps_the_rental_until_a_pending_transport_write_has_finished( )
    {
        using var wire = new HeldWriteStream( );
        using var batch = new CableBatchStream( wire );
        batch.BeginBatch( );
        var expected = "a pending websocket frame containing private viewer data"u8.ToArray( );
        await batch.WriteAsync( expected, TestContext.Current.CancellationToken );
        var flushing = batch.EndBatchAsync( TestContext.Current.CancellationToken ).AsTask( );
        await wire.Started.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );

        batch.Dispose( );
        wire.Release.SetResult( );
        await flushing.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );

        Assert.Equal( expected, wire.Delivered );
        await Assert.ThrowsAsync<ObjectDisposedException>( ( ) => batch.WriteAsync( expected, TestContext.Current.CancellationToken ).AsTask( ) );
    }

    [Fact]
    public async Task A_batch_is_one_transport_write_but_two_complete_websocket_messages( )
    {
        using var wire = new RecordingStream( );
        using var batch = new CableBatchStream( wire );
        using var server = WebSocket.CreateFromStream( batch, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan );
        batch.BeginBatch( );
        await server.SendAsync( "first"u8.ToArray( ).AsMemory( ), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken );
        await server.SendAsync( "second"u8.ToArray( ).AsMemory( ), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken );
        Assert.Equal( 0, wire.Writes );
        await batch.EndBatchAsync( TestContext.Current.CancellationToken );
        Assert.Equal( 1, wire.Writes );

        using var received = new MemoryStream( wire.ToArray( ) );
        using var client = WebSocket.CreateFromStream( received, isServer: false, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan );
        var buffer = new byte[32];
        foreach ( var expected in new[] { "first", "second" } )
        {
            var message = await client.ReceiveAsync( buffer.AsMemory( ), TestContext.Current.CancellationToken );
            Assert.True( message.EndOfMessage );
            Assert.Equal( WebSocketMessageType.Text, message.MessageType );
            Assert.Equal( expected, Encoding.UTF8.GetString( buffer, 0, message.Count ) );
        }
    }

    [Fact]
    public async Task Full_batches_flush_in_order_and_single_writes_pass_through( )
    {
        using var wire = new RecordingStream( );
        using var batch = new CableBatchStream( wire );
        var part = Enumerable.Range( 0, CableBatchStream.MaximumBatchBytes / 2 - 1 ).Select( index => ( byte ) (index % 251) ).ToArray( );
        await batch.WriteAsync( "start"u8.ToArray( ), TestContext.Current.CancellationToken );
        Assert.Equal( 1, wire.Writes );
        batch.BeginBatch( );
        for ( var index = 0; index < 5; index++ )
        {
            await batch.WriteAsync( part, TestContext.Current.CancellationToken );
        }

        await batch.EndBatchAsync( TestContext.Current.CancellationToken );
        Assert.Equal( 4, wire.Writes );
        Assert.All( wire.Sizes, size => Assert.True( size <= CableBatchStream.MaximumBatchBytes ) );
        Assert.Equal( "start"u8.ToArray( ).Concat( Enumerable.Repeat( part, 5 ).SelectMany( bytes => bytes ) ), wire.ToArray( ) );
    }

    private sealed class RecordingStream : MemoryStream
    {
        public int Writes { get; private set; }
        public List<int> Sizes { get; } = [];
        public override ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default )
        {
            Writes++;
            Sizes.Add( buffer.Length );
            Write( buffer.Span );
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HeldWriteStream : Stream
    {
        public TaskCompletionSource Started { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public byte[]? Delivered { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException( );
        public override long Position
        {
            get => throw new NotSupportedException( ); set => throw new NotSupportedException( );
        }
        public override async ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default )
        {
            Started.SetResult( );
            await Release.Task.WaitAsync( cancellationToken );
            Delivered = buffer.ToArray( );
        }
        public override void Flush( ) => throw new NotSupportedException( );
        public override int Read( byte[] buffer, int offset, int count ) => throw new NotSupportedException( );
        public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException( );
        public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
        public override void SetLength( long value ) => throw new NotSupportedException( );
    }
}
