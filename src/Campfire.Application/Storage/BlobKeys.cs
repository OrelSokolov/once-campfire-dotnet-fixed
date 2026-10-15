using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Campfire.Application.Storage;

/// <summary>Blob keys (<c>has_secure_token :key</c>) and blob checksums (<c>compute_checksum_in_chunks</c>).</summary>
[SuppressMessage(
    "Security",
    "CA5351:Do Not Use Broken Cryptographic Primitives",
    Justification = "The Base64 MD5 is the active_storage_blobs.checksum format; it detects corruption, not tampering." )]
public static class BlobKeys
{
    /// <summary><c>ActiveStorage::Blob::MINIMUM_TOKEN_LENGTH</c>.</summary>
    public const int Length = 28;

    private const string Base36 = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary><c>SecureRandom.base36(28)</c>: 28 characters drawn uniformly from <c>0-9a-z</c>.</summary>
    public static string Generate( ) => RandomNumberGenerator.GetString( Base36, Length );

    /// <summary>
    /// Whether <paramref name="key"/> can name a file under the disk service's root. Keys Active
    /// Storage generates are base36 (older Rails wrote base58), so anything but ASCII letters and
    /// digits is refused before it reaches a path, even though disk keys arrive signed.
    /// </summary>
    public static bool IsWellFormed( string? key ) => !string.IsNullOrEmpty( key ) && key.Length <= 256 && key.All( char.IsAsciiLetterOrDigit );

    /// <summary><c>OpenSSL::Digest::MD5.base64digest</c> of the whole content.</summary>
    public static string Checksum( ReadOnlySpan<byte> data ) => Convert.ToBase64String( MD5.HashData( data ) );

    /// <summary><see cref="Checksum(ReadOnlySpan{byte})"/> of a stream, read to its end.</summary>
    public static async Task<string> ChecksumAsync( Stream stream, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( stream );

        return Convert.ToBase64String( await MD5.HashDataAsync( stream, cancellationToken ) );
    }

    /// <summary><c>OpenSSL::Digest::MD5.file(path).base64digest</c>.</summary>
    public static async Task<string> ChecksumFileAsync( string path, CancellationToken cancellationToken )
    {
        var file = StorageFiles.OpenSequentialRead( path );
        await using ( file )
        {
            return await ChecksumAsync( file, cancellationToken );
        }
    }

    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/> and returns the checksum of the bytes copied.</summary>
    public static async Task<string> CopyWithChecksumAsync( Stream source, Stream destination, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( source );
        ArgumentNullException.ThrowIfNull( destination );

        using var checksum = CreateIncrementalChecksum( );
        var buffer = new byte[StorageFiles.BufferSize];
        int read;
        while ( (read = await source.ReadAsync( buffer, cancellationToken )) > 0 )
        {
            checksum.AppendData( buffer, 0, read );
            await destination.WriteAsync( buffer.AsMemory( 0, read ), cancellationToken );
        }

        return Convert.ToBase64String( checksum.GetHashAndReset( ) );
    }

    /// <summary>An MD5 computed while bytes stream past, for uploads that are checksummed as they are written.</summary>
    internal static IncrementalHash CreateIncrementalChecksum( ) => IncrementalHash.CreateHash( HashAlgorithmName.MD5 );
}
