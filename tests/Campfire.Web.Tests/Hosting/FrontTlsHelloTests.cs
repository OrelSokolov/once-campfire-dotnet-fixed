using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Security.Authentication;
using System.Text;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Connections;

namespace Campfire.Web.Tests.Hosting;

/// <summary>Bounded ClientHello peeking must preserve all TLS input across TCP and TLS-record fragmentation.</summary>
public sealed class FrontTlsHelloTests
{
    [Theory]
    [InlineData( "acme-tls/1", true )]
    [InlineData( "h2", false )]
    public async Task Fragmented_large_hellos_keep_the_original_transport_bytes( string protocol, bool isAcme )
    {
        var records = Records( Hello( protocol, padding: 9000 ), recordBytes: 3000 );
        var input = new Pipe( );
        var output = new Pipe( );
        await using var connection = new DefaultConnectionContext( );
        connection.Transport = new Transport( input.Reader, output.Writer );
        var inspect = FrontTlsHello.InspectAsync( connection, TimeSpan.FromSeconds( 10 ) );
        await input.Writer.WriteAsync( records.AsMemory( 0, 4096 ), TestContext.Current.CancellationToken );
        Assert.False( inspect.IsCompleted );
        await input.Writer.WriteAsync( records.AsMemory( 4096 ), TestContext.Current.CancellationToken );
        await inspect.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );

        Assert.Equal( isAcme, connection.Features.Get<FrontTlsHello>( )!.IsAcme );
        var retained = await input.Reader.ReadAsync( TestContext.Current.CancellationToken );
        Assert.Equal( records, retained.Buffer.ToArray( ) );
        input.Reader.AdvanceTo( retained.Buffer.End );
        await input.Reader.CompleteAsync( );
        await input.Writer.CompleteAsync( );
        await output.Reader.CompleteAsync( );
        await output.Writer.CompleteAsync( );
    }

    [Fact]
    public void Incomplete_records_wait_and_malformed_or_oversized_hellos_are_rejected( )
    {
        var records = Records( Hello( "acme-tls/1", 0 ), recordBytes: 3000 );
        var scratch = new byte[128 * 1024];
        Assert.False( FrontTlsHello.TryRead( records.AsSpan( 0, records.Length - 1 ), scratch, out _ ) );
        var malformed = ( byte[] ) records.Clone( );
        malformed[0] = 23;
        Assert.Throws<AuthenticationException>( ( ) => FrontTlsHello.TryRead( malformed, scratch, out _ ) );
        var oversized = ( byte[] ) records.Clone( );
        oversized[6] = 2; // Handshake's 24-bit length now exceeds 128 KiB.
        Assert.Throws<AuthenticationException>( ( ) => FrontTlsHello.TryRead( oversized, scratch, out _ ) );
    }

    private static byte[] Hello( string protocol, int padding )
    {
        var name = Encoding.ASCII.GetBytes( protocol );
        using var extensions = new MemoryStream( );
        Word( extensions, 16 );
        Word( extensions, name.Length + 3 );
        Word( extensions, name.Length + 1 );
        extensions.WriteByte( ( byte ) name.Length );
        extensions.Write( name );
        Word( extensions, 21 );
        Word( extensions, padding );
        extensions.Write( new byte[padding] );
        using var hello = new MemoryStream( );
        hello.Write( new byte[] { 3, 3 } );
        hello.Write( new byte[32] );
        hello.Write( new byte[] { 0, 0, 2, 0x13, 1, 1, 0 } );
        Word( hello, ( int ) extensions.Length );
        hello.Write( extensions.ToArray( ) );
        var bytes = hello.ToArray( );
        return new byte[] { 1, ( byte ) (bytes.Length >> 16), ( byte ) (bytes.Length >> 8), ( byte ) bytes.Length }.Concat( bytes ).ToArray( );
    }

    private static byte[] Records( byte[] hello, int recordBytes )
    {
        using var records = new MemoryStream( );
        for ( var offset = 0; offset < hello.Length; )
        {
            var length = Math.Min( recordBytes, hello.Length - offset );
            records.Write( new byte[] { 22, 3, 1 } );
            Word( records, length );
            records.Write( hello.AsSpan( offset, length ) );
            offset += length;
        }

        return records.ToArray( );
    }

    private static void Word( Stream stream, int value )
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian( bytes, ( ushort ) value );
        stream.Write( bytes );
    }

    private sealed class Transport( PipeReader input, PipeWriter output ) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }
}
