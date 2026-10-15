using System.Buffers.Binary;
using System.IO.Hashing;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>Length-prefixed full keys; text/member hash collisions still require an exact plaintext store match.</summary>
internal static class GzipKeyHash
{
    internal static ContentDigest TextIdentity( ReadOnlySpan<byte> plain )
    {
        var hash = XxHash128.HashToUInt128( plain );
        return new( ( ulong ) hash, ( ulong ) (hash >> 64), 0, 0 );
    }

    internal static UInt128 Piece( XxHash128 hash, bool fragment, ContentDigest identity, GzipHistory history, ReadOnlySpan<byte> glue )
    {
        hash.Reset( );
        AppendInt( hash, fragment ? 1 : 0 );
        AppendDigest( hash, identity );
        AppendBytes( hash, glue );
        AppendInt( hash, ( int ) history.Kind );
        AppendDigest( hash, history.Identity );
        AppendInt( hash, history.Parts.Length );
        foreach ( var part in history.Parts )
        {
            AppendDigest( hash, part.Identity );
            AppendBytes( hash, part.Glue.Span );
        }

        return hash.GetCurrentHashAsUInt128( );
    }

    internal static UInt128 Member( ContentDigest identity )
    {
        var hash = new XxHash128( );
        AppendInt( hash, 2 );
        AppendDigest( hash, identity );
        return hash.GetCurrentHashAsUInt128( );
    }

    private static void AppendDigest( XxHash128 hash, ContentDigest digest )
    {
        Span<byte> bytes = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian( bytes, digest.FirstWord );
        BinaryPrimitives.WriteUInt64LittleEndian( bytes[8..], digest.SecondWord );
        BinaryPrimitives.WriteUInt64LittleEndian( bytes[16..], digest.ThirdWord );
        BinaryPrimitives.WriteUInt64LittleEndian( bytes[24..], digest.FourthWord );
        hash.Append( bytes );
    }

    private static void AppendBytes( XxHash128 hash, ReadOnlySpan<byte> bytes )
    {
        AppendInt( hash, bytes.Length );
        hash.Append( bytes );
    }

    private static void AppendInt( XxHash128 hash, int value )
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian( bytes, value );
        hash.Append( bytes );
    }
}
