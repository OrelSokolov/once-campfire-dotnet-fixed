using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Campfire.Application.Data;
using Campfire.Web.Views.Helpers;

namespace Campfire.Web.Views.Caching;

public static partial class FragmentKeys
{
    // The final key contains only fixed vocabulary, an Int64 id, a timestamp and a 128-bit digest.
    internal const int MessageKeyCapacity = 192;

    /// <summary>A digest's length in a key: its first 128 bits as lowercase hex.</summary>
    private const int DigestChars = 32;

    /// <summary>The digest input a writer spells on the stack before it rents.</summary>
    private const int StackCapacity = 512;

    private const string MessagePrefix = "views/messages/_message/";

    /// <summary>Writes the message fragment's key into <paramref name="key"/> and returns its length.</summary>
    internal static int WriteMessageKey( MessageCacheInputs inputs, Span<char> key )
    {
        MessagePrefix.CopyTo( key );
        var written = MessagePrefix.Length + WriteRecord( key[MessagePrefix.Length..], inputs.Message, TableNames.Messages );
        key[written++] = '/';
        PresentationVersion.CopyTo( key[written..] );
        written += PresentationVersion.Length;
        key[written++] = '/';
        if ( inputs.DependencyMemo is { } memo && inputs.Boosters.Count == 0 )
        {
            memo.Get( inputs ).CopyTo( key[written..] );
        }
        else
        {
            WriteMessageDependencyDigest( inputs, key.Slice( written, DigestChars ) );
        }

        return written + DigestChars;
    }

    private static void WriteMessageDependencyDigest( MessageCacheInputs inputs, Span<char> digest )
    {
        var capacity = checked(inputs.RoomName.Length + inputs.PublicBaseUrl.Length + 160 + inputs.Boosters.Count * 140);
        char[]? rental = capacity > StackCapacity ? ArrayPool<char>.Shared.Rent( capacity ) : null;
        Span<char> parts = rental is null ? stackalloc char[StackCapacity] : rental;
        var position = 0;
        try
        {
            position += WriteRecord( parts, inputs.Creator, TableNames.Users );
            parts[position++] = '\n';
            inputs.RoomName.CopyTo( parts[position..] );
            position += inputs.RoomName.Length;
            parts[position++] = '\n';
            inputs.PublicBaseUrl.CopyTo( parts[position..] );
            position += inputs.PublicBaseUrl.Length;
            foreach ( var (boost, booster) in inputs.Boosters )
            {
                parts[position++] = '\n';
                position += WriteRecord( parts[position..], boost, TableNames.Boosts );
                parts[position++] = ' ';
                position += WriteRecord( parts[position..], booster, TableNames.Users );
            }

            WriteDigest( parts[..position], digest );
        }
        finally
        {
            parts[..position].Clear( );
            if ( rental is not null )
            {
                ArrayPool<char>.Shared.Return( rental );
            }
        }
    }

    /// <summary>
    /// Unboosted messages can share the same dependency suffix even when their message record prefix differs.
    /// This lives within one presenter; changed creator versions, room names or origins always use another entry.
    /// Boosted messages retain the full ordered dependency calculation.
    /// </summary>
    internal sealed class MessageDependencyMemo
    {
        private const int MaximumEntries = 128;
        private readonly Dictionary<(RecordVersion Creator, string RoomName, string Origin), string> _digests = [];

        public string Get( MessageCacheInputs inputs )
        {
            var identity = (inputs.Creator, inputs.RoomName, inputs.PublicBaseUrl);
            if ( _digests.TryGetValue( identity, out var digest ) )
            {
                return digest;
            }

            Span<char> output = stackalloc char[DigestChars];
            WriteMessageDependencyDigest( inputs, output );
            digest = new string( output );
            if ( _digests.Count < MaximumEntries )
            {
                _digests.Add( identity, digest );
            }

            return digest;
        }
    }

    private static int WriteRecord( Span<char> output, RecordVersion record, string table )
    {
        table.CopyTo( output );
        var position = table.Length;
        output[position++] = '/';
        if ( !record.Id.TryFormat( output[position..], out var written, provider: CultureInfo.InvariantCulture ) )
        {
            throw new InvalidOperationException( "Fragment key id buffer is too small." );
        }

        position += written;
        output[position++] = '-';
        if ( !record.UpdatedAt.UtcDateTime.TryFormat( output[position..], out written, RubyText.CacheTimestampFormat, CultureInfo.InvariantCulture ) )
        {
            throw new InvalidOperationException( "Fragment key timestamp buffer is too small." );
        }

        return position + written;
    }

    /// <summary>
    /// The SHA-256 of <paramref name="parts"/>' UTF-8, its first 128 bits written into <paramref name="output"/> as
    /// <see cref="DigestChars"/> lowercase hex characters.
    /// </summary>
    private static void WriteDigest( ReadOnlySpan<char> parts, Span<char> output )
    {
        var capacity = Encoding.UTF8.GetMaxByteCount( parts.Length );
        byte[]? rental = capacity > StackCapacity ? ArrayPool<byte>.Shared.Rent( capacity ) : null;
        Span<byte> bytes = rental is null ? stackalloc byte[StackCapacity] : rental;
        var written = 0;
        try
        {
            written = Encoding.UTF8.GetBytes( parts, bytes );
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData( bytes[..written], digest );
            if ( !Convert.TryToHexStringLower( digest[..(DigestChars / 2)], output, out var formatted ) || formatted != output.Length )
            {
                throw new InvalidOperationException( "Fragment digest buffer is too small." );
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory( bytes[..written] );
            if ( rental is not null )
            {
                ArrayPool<byte>.Shared.Return( rental );
            }
        }
    }
}
