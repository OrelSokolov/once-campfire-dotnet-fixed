using System.Buffers;
using Campfire.Application.Storage.Marcel;

namespace Campfire.Application.Storage.Disk;

/// <summary>
/// A complete file in the disk service's staging directory, with the checksum, size and leading
/// bytes (enough for Marcel) of exactly what was written. <see cref="DiskService.Publish"/> renames
/// it to its key's path in one step, so a blob's file is either absent or whole. Disposing a file
/// that was never published deletes it.
/// </summary>
public sealed class StagedFile : IDisposable
{
    private StagedFile( string path, long length, string checksum, byte[] head )
    {
        Path = path;
        Length = length;
        Checksum = checksum;
        Head = head;
    }

    /// <summary>Where the bytes are now: the staging file, or the key's path once published.</summary>
    public string Path { get; private set; }

    public long Length { get; }

    /// <summary>The Base64 MD5 of the bytes.</summary>
    public string Checksum { get; }

    /// <summary>The first <see cref="MarcelMimeType.MagicWindow"/> bytes (all of them for a shorter file).</summary>
    public ReadOnlyMemory<byte> Head { get; }

    public bool IsPublished { get; private set; }

    /// <summary>Deletes the staging file unless it was published.</summary>
    public void Dispose( )
    {
        if ( !IsPublished )
        {
            File.Delete( Path );
        }
    }

    internal void MarkPublished( string destination )
    {
        Path = destination;
        IsPublished = true;
    }

    /// <summary>
    /// Copies <paramref name="source"/> to a new file at <paramref name="path"/>, checksumming as it
    /// goes and refusing more than <paramref name="maxBytes"/>. The file is flushed to disk before
    /// this returns; on any failure it is deleted.
    /// </summary>
    internal static async Task<StagedFile> WriteAsync( string path, Stream source, long maxBytes, CancellationToken cancellationToken )
    {
        try
        {
            var output = StorageFiles.CreateNew( path );
            await using ( output )
            {
                var digest = await DigestAsync( source, maxBytes, output, cancellationToken );
                await output.FlushAsync( cancellationToken );
                output.Flush( flushToDisk: true );

                return new StagedFile( path, digest.Length, digest.Checksum, digest.Head );
            }
        }
        catch
        {
            File.Delete( path );
            throw;
        }
    }

    /// <summary>
    /// Takes ownership of a file something else wrote into the staging directory (a variant libvips
    /// saved, a frame ffmpeg drew): checksums it in place and flushes it to disk.
    /// </summary>
    internal static async Task<StagedFile> AdoptAsync( string path, long maxBytes, CancellationToken cancellationToken )
    {
        try
        {
            var file = new FileStream(
                path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, StorageFiles.BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan );
            await using ( file )
            {
                var digest = await DigestAsync( file, maxBytes, copyTo: null, cancellationToken );
                file.Flush( flushToDisk: true );

                return new StagedFile( path, digest.Length, digest.Checksum, digest.Head );
            }
        }
        catch
        {
            File.Delete( path );
            throw;
        }
    }

    /// <summary>Reads <paramref name="source"/> to its end: MD5, length and head, optionally copying the bytes.</summary>
    private static async Task<(long Length, string Checksum, byte[] Head)> DigestAsync(
        Stream source, long maxBytes, Stream? copyTo, CancellationToken cancellationToken )
    {
        var buffer = ArrayPool<byte>.Shared.Rent( StorageFiles.BufferSize );
        try
        {
            using var checksum = BlobKeys.CreateIncrementalChecksum( );
            var head = new byte[MarcelMimeType.MagicWindow];
            var headLength = 0;
            long length = 0;
            int read;
            while ( (read = await source.ReadAsync( buffer.AsMemory( ), cancellationToken )) > 0 )
            {
                length += read;
                if ( length > maxBytes )
                {
                    throw new StorageException( StorageFailure.LimitExceeded, $"The file is larger than the {maxBytes}-byte limit." );
                }

                checksum.AppendData( buffer, 0, read );
                var take = Math.Min( read, head.Length - headLength );
                buffer.AsSpan( 0, take ).CopyTo( head.AsSpan( headLength ) );
                headLength += take;
                if ( copyTo is not null )
                {
                    await copyTo.WriteAsync( buffer.AsMemory( 0, read ), cancellationToken );
                }
            }

            return (length, Convert.ToBase64String( checksum.GetHashAndReset( ) ), head[..headLength]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return( buffer );
        }
    }
}
