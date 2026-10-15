using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Campfire.Web.Views.Rendering;

/// <summary>Value identity for immutable markup; fragments compute it once at construction.</summary>
internal readonly record struct ContentDigest( ulong FirstWord, ulong SecondWord, ulong ThirdWord, ulong FourthWord )
{
    internal void WriteTo( Span<byte> bytes )
    {
        BinaryPrimitives.WriteUInt64LittleEndian( bytes, FirstWord );
        BinaryPrimitives.WriteUInt64LittleEndian( bytes[8..], SecondWord );
        BinaryPrimitives.WriteUInt64LittleEndian( bytes[16..], ThirdWord );
        BinaryPrimitives.WriteUInt64LittleEndian( bytes[24..], FourthWord );
    }

    internal static ContentDigest Of( ReadOnlySpan<byte> bytes )
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData( bytes, digest );
        return new ContentDigest(
            BinaryPrimitives.ReadUInt64LittleEndian( digest ),
            BinaryPrimitives.ReadUInt64LittleEndian( digest[8..] ),
            BinaryPrimitives.ReadUInt64LittleEndian( digest[16..] ),
            BinaryPrimitives.ReadUInt64LittleEndian( digest[24..] ) );
    }
}
