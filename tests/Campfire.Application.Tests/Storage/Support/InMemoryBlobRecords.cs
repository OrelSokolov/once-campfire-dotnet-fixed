using Campfire.Application.Storage;

namespace Campfire.Application.Tests.Storage.Support;

/// <summary>
/// <see cref="IBlobRecords"/> over lists, with the schema's unique indexes
/// (<c>index_active_storage_blobs_on_key</c>, <c>index_active_storage_variant_records_uniqueness</c>,
/// <c>index_active_storage_attachments_uniqueness</c>) enforced. Ids count up from 1 per table, as
/// SQLite's <c>AUTOINCREMENT</c> does in a fresh database. One lock makes each call atomic, the way
/// each call is one transaction in the real implementation.
/// </summary>
internal sealed class InMemoryBlobRecords : IBlobRecords
{
    private readonly Lock _lock = new( );
    private readonly List<Blob> _blobs = [];
    private readonly List<AttachmentRow> _attachments = [];
    private readonly List<VariantRecordRow> _variantRecords = [];

    public IReadOnlyList<Blob> Blobs
    {
        get
        {
            lock ( _lock )
            {
                return [.. _blobs];
            }
        }
    }

    public IReadOnlyList<AttachmentRow> Attachments
    {
        get
        {
            lock ( _lock )
            {
                return [.. _attachments];
            }
        }
    }

    public IReadOnlyList<VariantRecordRow> VariantRecords
    {
        get
        {
            lock ( _lock )
            {
                return [.. _variantRecords];
            }
        }
    }

    /// <summary>Runs before a variant or preview is recorded: lets a test create the competing row first.</summary>
    public Func<Task>? BeforeRecording { get; set; }

    public Task<Blob> InsertBlobAsync( NewBlob blob, DateTimeOffset createdAt, CancellationToken cancellationToken )
    {
        lock ( _lock )
        {
            return Task.FromResult( InsertBlob( blob, createdAt ) );
        }
    }

    public Task<Blob?> AttachedBlobAsync( AttachmentOwner owner, string name, CancellationToken cancellationToken )
    {
        lock ( _lock )
        {
            return Task.FromResult( Attached( owner, name ) );
        }
    }

    public Task<Blob?> VariantImageAsync( long blobId, string variationDigest, CancellationToken cancellationToken )
    {
        lock ( _lock )
        {
            return Task.FromResult( VariantImage( blobId, variationDigest ) );
        }
    }

    public async Task<Blob?> RecordVariantAsync(
        long blobId,
        string variationDigest,
        NewBlob image,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken )
    {
        await RunBeforeRecordingAsync( );
        lock ( _lock )
        {
            if ( _variantRecords.Any( row => row.BlobId == blobId && row.VariationDigest == variationDigest ) )
            {
                return null;
            }

            var record = new VariantRecordRow( _variantRecords.Count + 1, blobId, variationDigest );
            _variantRecords.Add( record );
            var blob = InsertBlob( image, createdAt );
            InsertAttachment( "image", AttachmentOwner.ForVariantRecord( record.Id ), blob.Id, createdAt );

            return blob;
        }
    }

    public async Task<Blob?> RecordPreviewImageAsync( long blobId, NewBlob image, DateTimeOffset createdAt, CancellationToken cancellationToken )
    {
        await RunBeforeRecordingAsync( );
        lock ( _lock )
        {
            if ( Attached( AttachmentOwner.ForBlob( blobId ), "preview_image" ) is not null )
            {
                return null;
            }

            var blob = InsertBlob( image, createdAt );
            InsertAttachment( "preview_image", AttachmentOwner.ForBlob( blobId ), blob.Id, createdAt );

            return blob;
        }
    }

    private async Task RunBeforeRecordingAsync( )
    {
        var hook = BeforeRecording;
        BeforeRecording = null;
        if ( hook is not null )
        {
            await hook( );
        }
    }

    private Blob InsertBlob( NewBlob blob, DateTimeOffset createdAt )
    {
        if ( _blobs.Any( existing => existing.Key == blob.Key ) )
        {
            throw new InvalidOperationException( $"UNIQUE constraint failed: active_storage_blobs.key ({blob.Key})" );
        }

        var saved = blob.ToBlob( _blobs.Count + 1, createdAt );
        _blobs.Add( saved );

        return saved;
    }

    private long InsertAttachment( string name, AttachmentOwner owner, long blobId, DateTimeOffset createdAt )
    {
        if ( _attachments.Any( row => row.Owner == owner && row.Name == name && row.BlobId == blobId ) )
        {
            throw new InvalidOperationException( "UNIQUE constraint failed: index_active_storage_attachments_uniqueness" );
        }

        var row = new AttachmentRow( _attachments.Count + 1, name, owner, blobId, createdAt );
        _attachments.Add( row );

        return row.Id;
    }

    private Blob? Attached( AttachmentOwner owner, string name )
    {
        var attachment = _attachments.Where( row => row.Owner == owner && row.Name == name ).OrderBy( row => row.Id ).FirstOrDefault( );

        return attachment is null ? null : _blobs.FirstOrDefault( blob => blob.Id == attachment.BlobId );
    }

    private Blob? VariantImage( long blobId, string variationDigest )
    {
        var record = _variantRecords.FirstOrDefault( row => row.BlobId == blobId && row.VariationDigest == variationDigest );

        return record is null ? null : Attached( AttachmentOwner.ForVariantRecord( record.Id ), "image" );
    }

    internal sealed record AttachmentRow( long Id, string Name, AttachmentOwner Owner, long BlobId, DateTimeOffset CreatedAt );

    internal sealed record VariantRecordRow( long Id, long BlobId, string VariationDigest );
}
