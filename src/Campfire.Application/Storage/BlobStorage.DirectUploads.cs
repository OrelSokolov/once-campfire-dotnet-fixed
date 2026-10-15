using Campfire.Application.Data.Records;
using Campfire.Application.Storage.Marcel;

namespace Campfire.Application.Storage;

/// <summary>
/// The file half of the direct-upload flow: identifying a blob attached by its signed id, and receiving the bytes the
/// disk service's <c>PUT</c> brings.
/// </summary>
public sealed partial class BlobStorage
{
    /// <summary><c>Identifiable#download_identifiable_chunk</c>: <c>download_chunk key, 0...4.kilobytes</c>.</summary>
    public const int IdentifiableChunkBytes = 4096;

    /// <summary>
    /// <c>Blob#identify_without_saving</c>, which <c>Attached::Changes::CreateOne</c> runs on every blob it attaches:
    /// nothing for a blob already identified; otherwise the content type Marcel gives the first 4 KiB (no bytes for an
    /// empty blob), the filename and the declared type, and the metadata with <c>identified: true</c>. A blob whose
    /// file isn't there raises <see cref="StorageFailure.FileNotFound"/>, as the disk service raises
    /// <c>ActiveStorage::FileNotFoundError</c>.
    /// </summary>
    public async Task<BlobIdentity?> IdentifyWithoutSavingAsync( Blob blob, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( blob );

        if ( blob.Metadata.IsIdentified )
        {
            return null;
        }

        var chunk = blob.ByteSize > 0 ? await Disk.DownloadChunkAsync( blob.Key, 0, IdentifiableChunkBytes, cancellationToken ) : [];
        var contentType = MarcelMimeType.Identify( chunk, blob.Filename.Sanitized, blob.ContentType );
        var metadata = blob.Metadata.With( BlobMetadata.IdentifiedKey, true );
        return new BlobIdentity( contentType, metadata.ToJson( ) );
    }

    /// <summary>
    /// <c>DiskService#upload(key, io, checksum:)</c> for <c>DiskController#update</c>: the bytes are staged, checked
    /// against <paramref name="checksum"/> (<see cref="StorageFailure.Integrity"/> when they differ, nothing kept) and
    /// published at the key. A repeated <c>PUT</c> finds the file in place: Rails writes the same bytes over it (the
    /// token fixes the checksum), so the staged copy is dropped instead, since a published file is never replaced.
    /// The key comes from a verified token; a malformed one is refused all the same.
    /// </summary>
    public async Task ReceiveDirectUploadAsync( string key, Stream content, string checksum, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( content );

        if ( !BlobKeys.IsWellFormed( key ) )
        {
            throw new StorageException( StorageFailure.FileNotFound, "The direct upload's key can't name a file." );
        }

        using var staged = await Disk.StageAsync( content, _options.MaxUploadBytes, cancellationToken );
        if ( staged.Checksum != checksum )
        {
            throw new StorageException( StorageFailure.Integrity, "The uploaded bytes don't match the checksum." );
        }

        if ( !Disk.Exists( key ) )
        {
            Disk.Publish( staged, key );
        }
    }
}
