using System.Buffers.Binary;
using System.Security.Cryptography;
using Campfire.Web.Http;

namespace Campfire.Web.Views.Rendering;

/// <summary>Unambiguous ordered content identities; immutable fragments contribute their once-computed digest.</summary>
internal static class PageValidator
{
    internal static string Compute( ReadOnlySpan<byte> bytes, IReadOnlyList<RecordedFragment> fragments )
    {
        if ( fragments.Count == 0 )
        {
            return Convert.ToHexStringLower( SHA256.HashData( bytes ).AsSpan( 0, 16 ) );
        }

        using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        foreach ( var part in PageParts.Split( bytes.Length, fragments ) )
        {
            if ( part.Fragment is { } fragment )
            {
                hash.AppendData( "F"u8 );
                Length( hash, part.Glue );
                hash.AppendData( bytes.Slice( part.Offset, part.Glue ) );
                Length( hash, fragment.Bytes.Length );
                fragment.Digest.WriteTo( digest );
            }
            else
            {
                hash.AppendData( "T"u8 );
                Length( hash, part.Length );
                SHA256.HashData( part.Bytes( bytes ), digest );
            }

            hash.AppendData( digest );
        }

        hash.GetHashAndReset( digest );
        return Convert.ToHexStringLower( digest[..16] );
    }

    private static void Length( IncrementalHash hash, int length )
    {
        Span<byte> bytes = stackalloc byte[sizeof( long )];
        BinaryPrimitives.WriteInt64LittleEndian( bytes, length );
        hash.AppendData( bytes );
    }
}
