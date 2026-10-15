using System.Buffers;
using System.Buffers.Binary;
using System.Security.Authentication;
using System.Text;
using Microsoft.AspNetCore.Connections;

namespace Campfire.Web.Hosting;

/// <summary>
/// Kestrel's TLS callback exposes SNI but not offered ALPN. Peek at the bounded ClientHello without consuming it,
/// so challenge certificates are selected only for acme-tls/1 and ordinary visitors keep their real certificate.
/// </summary>
internal sealed record FrontTlsHello( bool IsAcme )
{
    private const int MaximumHelloBytes = 128 * 1024;
    private const int InitialHelloBytes = 4 * 1024;
    private const byte HandshakeRecord = 22;
    private const byte ClientHello = 1;
    private const int AlpnExtension = 16;
    private static readonly byte[] AcmeName = Encoding.ASCII.GetBytes( FrontServerOptions.AcmeProtocol );

    internal static async Task InspectAsync( ConnectionContext connection, TimeSpan timeout )
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource( connection.ConnectionClosed );
        if ( timeout > TimeSpan.Zero )
        {
            cancellation.CancelAfter( timeout );
        }

        var scratch = ArrayPool<byte>.Shared.Rent( InitialHelloBytes * 2 );
        try
        {
            while ( true )
            {
                var read = await connection.Transport.Input.ReadAsync( cancellation.Token );
                var buffer = read.Buffer;
                if ( buffer.Length > MaximumHelloBytes )
                {
                    connection.Transport.Input.AdvanceTo( buffer.Start, buffer.Start );
                    throw new AuthenticationException( "TLS ClientHello exceeds the size limit." );
                }

                if ( buffer.Length > scratch.Length / 2 )
                {
                    // Idle TLS clients must not each reserve two maximum-sized ClientHello buffers.
                    var replacement = ArrayPool<byte>.Shared.Rent( checked(( int ) buffer.Length * 2) );
                    ArrayPool<byte>.Shared.Return( scratch, clearArray: true );
                    scratch = replacement;
                }

                buffer.CopyTo( scratch );
                if ( TryRead( scratch.AsSpan( 0, ( int ) buffer.Length ), scratch.AsSpan( scratch.Length / 2 ), out var isAcme ) )
                {
                    connection.Features.Set( new FrontTlsHello( isAcme ) );
                    connection.Transport.Input.AdvanceTo( buffer.Start, buffer.Start );
                    return;
                }

                connection.Transport.Input.AdvanceTo( buffer.Start, buffer.End );
                if ( read.IsCompleted || read.IsCanceled )
                {
                    throw new AuthenticationException( "Incomplete TLS ClientHello." );
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return( scratch, clearArray: true );
        }
    }

    internal static bool TryRead( ReadOnlySpan<byte> records, Span<byte> handshake, out bool isAcme )
    {
        isAcme = false;
        var copied = 0;
        var position = 0;
        while ( records.Length - position >= 5 )
        {
            if ( records[position] != HandshakeRecord )
            {
                throw new AuthenticationException( "Expected a TLS handshake record." );
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian( records.Slice( position + 3, 2 ) );
            if ( records.Length - position - 5 < length )
            {
                return false;
            }

            if ( length > handshake.Length - copied )
            {
                throw new AuthenticationException( "TLS ClientHello exceeds the size limit." );
            }

            records.Slice( position + 5, length ).CopyTo( handshake[copied..] );
            copied += length;
            position += 5 + length;
            if ( copied < 4 )
            {
                continue;
            }

            if ( handshake[0] != ClientHello )
            {
                throw new AuthenticationException( "Expected TLS ClientHello." );
            }

            var helloLength = (handshake[1] << 16) | (handshake[2] << 8) | handshake[3];
            if ( helloLength > MaximumHelloBytes - 4 )
            {
                throw new AuthenticationException( "TLS ClientHello exceeds the size limit." );
            }

            if ( copied >= helloLength + 4 )
            {
                isAcme = HasAcmeProtocol( handshake.Slice( 4, helloLength ) );
                return true;
            }
        }

        return false;
    }

    private static bool HasAcmeProtocol( ReadOnlySpan<byte> hello )
    {
        var position = 34; // legacy version and 32-byte random.
        SkipVector( hello, ref position, 1 );
        SkipVector( hello, ref position, 2 );
        SkipVector( hello, ref position, 1 );
        if ( position == hello.Length )
        {
            return false;
        }

        var extensionLength = ReadLength( hello, ref position, 2 );
        Require( hello, position, extensionLength );
        var end = position + extensionLength;
        while ( position < end )
        {
            var type = ReadLength( hello[..end], ref position, 2 );
            var length = ReadLength( hello[..end], ref position, 2 );
            Require( hello[..end], position, length );
            if ( type == AlpnExtension && HasAcmeName( hello.Slice( position, length ) ) )
            {
                return true;
            }

            position += length;
        }

        return false;
    }

    private static bool HasAcmeName( ReadOnlySpan<byte> value )
    {
        var position = 0;
        var length = ReadLength( value, ref position, 2 );
        Require( value, position, length );
        var end = position + length;
        while ( position < end )
        {
            var nameLength = ReadLength( value[..end], ref position, 1 );
            Require( value[..end], position, nameLength );
            if ( value.Slice( position, nameLength ).SequenceEqual( AcmeName ) )
            {
                return true;
            }

            position += nameLength;
        }

        return false;
    }

    private static void SkipVector( ReadOnlySpan<byte> value, ref int position, int lengthBytes )
    {
        var length = ReadLength( value, ref position, lengthBytes );
        Require( value, position, length );
        position += length;
    }

    private static int ReadLength( ReadOnlySpan<byte> value, ref int position, int lengthBytes )
    {
        Require( value, position, lengthBytes );
        var length = lengthBytes == 1 ? value[position] : BinaryPrimitives.ReadUInt16BigEndian( value.Slice( position, 2 ) );
        position += lengthBytes;
        return length;
    }

    private static void Require( ReadOnlySpan<byte> value, int position, int length )
    {
        if ( position < 0 || length < 0 || position > value.Length - length )
        {
            throw new AuthenticationException( "Malformed TLS ClientHello." );
        }
    }
}
