using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Campfire.Web.Cable;

/// <summary>One owned JSON message with lazy immutable wire variants shared by its identifier group.</summary>
internal sealed class CableFrame( byte[] payload )
{
    /// <summary>Payloads shorter than this are sent uncompressed even on a deflate connection.</summary>
    internal const int CompressionThreshold = 256;

    private const byte TextFrame = WebSocketFrameHeader.Final | WebSocketFrameHeader.TextOpcode;
    private const byte CompressedTextFrame = TextFrame | WebSocketFrameHeader.Compressed;

    /// <summary>zlib's default compression level.</summary>
    private const int DeflateLevel = 6;

    private const int DeflateBufferBytes = 16 * 1024;

    private readonly Lock _gate = new( );
    private byte[]? _plain;
    private byte[]? _deflated;

    public ReadOnlyMemory<byte> Payload => payload;

    public ReadOnlyMemory<byte> Wire( bool deflate )
    {
        if ( !deflate || payload.Length < CompressionThreshold )
        {
            if ( Volatile.Read( ref _plain ) is { } plain )
            {
                return plain;
            }

            lock ( _gate )
            {
                return _plain ??= Frame( payload, compressed: false );
            }
        }

        if ( Volatile.Read( ref _deflated ) is { } cached )
        {
            return cached;
        }

        lock ( _gate )
        {
            return _deflated ??= Frame( Compress( payload ), compressed: true );
        }
    }

    /// <summary>One final text frame holding <paramref name="content"/>, marked compressed when it is.</summary>
    private static byte[] Frame( ReadOnlySpan<byte> content, bool compressed )
    {
        var headerLength = HeaderLength( content.Length );
        var wire = new byte[checked(headerLength + content.Length)];
        wire[0] = compressed ? CompressedTextFrame : TextFrame;
        if ( headerLength == WebSocketFrameHeader.ShortHeaderLength )
        {
            wire[1] = ( byte ) content.Length;
        }
        else if ( headerLength == WebSocketFrameHeader.MediumHeaderLength )
        {
            wire[1] = WebSocketFrameHeader.MediumLengthMarker;
            BinaryPrimitives.WriteUInt16BigEndian( wire.AsSpan( 2 ), ( ushort ) content.Length );
        }
        else
        {
            wire[1] = WebSocketFrameHeader.LongLengthMarker;
            BinaryPrimitives.WriteUInt64BigEndian( wire.AsSpan( 2 ), ( ulong ) content.Length );
        }

        content.CopyTo( wire.AsSpan( headerLength ) );
        return wire;
    }

    private static int HeaderLength( int payloadLength )
    {
        if ( payloadLength <= WebSocketFrameHeader.MaxShortPayloadLength )
        {
            return WebSocketFrameHeader.ShortHeaderLength;
        }

        if ( payloadLength <= ushort.MaxValue )
        {
            return WebSocketFrameHeader.MediumHeaderLength;
        }

        return WebSocketFrameHeader.LongHeaderLength;
    }

    private static byte[] Compress( ReadOnlySpan<byte> source )
    {
        using var encoder = new DeflateEncoder( DeflateLevel );
        var output = new ArrayBufferWriter<byte>( );
        Span<byte> buffer = stackalloc byte[DeflateBufferBytes];
        while ( !source.IsEmpty )
        {
            var status = encoder.Compress( source, buffer, out var consumed, out var written, isFinalBlock: false );
            if ( status == OperationStatus.InvalidData || consumed == 0 && written == 0 )
            {
                throw new InvalidDataException( "Cable deflate made no progress." );
            }

            output.Write( buffer[..written] );
            source = source[consumed..];
        }

        while ( true )
        {
            var status = encoder.Flush( buffer, out var written );
            output.Write( buffer[..written] );
            if ( status == OperationStatus.Done )
            {
                break;
            }

            if ( status != OperationStatus.DestinationTooSmall || written == 0 )
            {
                throw new InvalidDataException( "Cable deflate flush made no progress." );
            }
        }

        // RFC 7692 §7.2.1: the message is sent without the sync flush's empty block.
        var bytes = output.WrittenSpan;
        ReadOnlySpan<byte> syncFlushMarker = [0, 0, 255, 255];
        if ( !bytes.EndsWith( syncFlushMarker ) )
        {
            throw new InvalidDataException( "Cable deflate has no sync-flush marker." );
        }

        return bytes[..^syncFlushMarker.Length].ToArray( );
    }
}
