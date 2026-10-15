using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Storage;
using Campfire.Application.Tests.Storage.Support;
using Campfire.Application.Tests.Support;

namespace Campfire.Application.Tests.Storage;

/// <summary>
/// <see cref="BlobRecords"/>, the <see cref="IBlobRecords"/> the app runs on, against real SQLite; the contract tests
/// also run on <see cref="InMemoryBlobRecords"/>, which the storage tests use in its place, so the two agree. The
/// reads and writes outside the interface run on SQLite alone.
/// </summary>
public sealed class BlobRecordsTests
{
    private const string Sqlite = "sqlite";

    private const string InMemory = "memory";

    /// <summary>A creation time with microseconds, which Rails writes as <c>.123456</c>.</summary>
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset( 2026, 9, 26, 12, 34, 56, TimeSpan.Zero ).AddTicks( 1_234_560 );

    private static CancellationToken Cancel => TestDatabase.Cancellation;

    public static TheoryData<string> Implementations => [Sqlite, InMemory];

    [Fact]
    public async Task Inserted_blobs_are_found_by_id( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var records = new BlobRecords( testDatabase.Database );

        var blob = await records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel );

        Assert.Equal( blob, await testDatabase.ReadAsync( db => BlobRecords.FindById( db, blob.Id ) ), BlobComparer.Instance );
        Assert.Null( await testDatabase.ReadAsync( db => BlobRecords.FindById( db, blob.Id + 1 ) ) );
        Assert.Equal( ("moon.png", "image/png", 1234L, "abc=="), (blob.Filename.Raw, blob.ContentType, blob.ByteSize, blob.Checksum) );
        Assert.True( blob.Metadata.IsIdentified );
    }

    [Fact]
    public async Task An_owner_has_its_earliest_attachment_and_a_blob_lists_its_owners_oldest_first( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var records = new BlobRecords( testDatabase.Database );
        var first = await records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel );
        var second = await records.InsertBlobAsync( Image( "key-2" ), CreatedAt, Cancel );
        var owner = new AttachmentOwner( "User", 7 );

        await InsertAttachmentAsync( testDatabase, "avatar", owner, first.Id );
        await InsertAttachmentAsync( testDatabase, "avatar", owner, second.Id );
        await InsertAttachmentAsync( testDatabase, "attachment", new AttachmentOwner( "Message", 3 ), first.Id );

        Assert.Equal( first.Id, (await records.AttachedBlobAsync( owner, "avatar", Cancel ))?.Id );
        Assert.Null( await records.AttachedBlobAsync( owner, "logo", Cancel ) );
        Assert.Equal( [owner, new AttachmentOwner( "Message", 3 )], await testDatabase.ReadAsync( db => BlobRecords.Owners( db, first.Id ) ) );
    }

    [Fact]
    public async Task Metadata_updates_replace_the_stored_metadata( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var records = new BlobRecords( testDatabase.Database );
        var blob = await records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel );
        var analyzed = blob.Metadata.With( "analyzed", true ).With( "width", 640 );

        await testDatabase.WriteAsync( scope => BlobRecords.UpdateMetadata( scope.Db, blob.Id, analyzed.ToJson( ) ) );

        Assert.Equal( analyzed.ToJson( ), (await testDatabase.ReadAsync( db => BlobRecords.FindById( db, blob.Id ) ))!.Metadata.ToJson( ) );
    }

    [Theory]
    [MemberData( nameof( Implementations ) )]
    public async Task A_variant_is_recorded_once( string implementation )
    {
        using var subject = await Subject.CreateAsync( implementation );
        var blob = await subject.Records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel );

        var image = await subject.Records.RecordVariantAsync( blob.Id, "digest", Image( "variant-1" ), CreatedAt, Cancel );
        var again = await subject.Records.RecordVariantAsync( blob.Id, "digest", Image( "variant-2" ), CreatedAt, Cancel );

        Assert.NotNull( image );
        Assert.Null( again );
        Assert.Equal( image.Id, (await subject.Records.VariantImageAsync( blob.Id, "digest", Cancel ))?.Id );
        Assert.Null( await subject.Records.VariantImageAsync( blob.Id, "other", Cancel ) );
        Assert.False( await subject.HasBlobAsync( "variant-2" ) );
    }

    [Theory]
    [MemberData( nameof( Implementations ) )]
    public async Task A_preview_image_is_recorded_once( string implementation )
    {
        using var subject = await Subject.CreateAsync( implementation );
        var video = await subject.Records.InsertBlobAsync( Image( "video-1" ) with
        {
            ContentType = "video/mp4"
        }, CreatedAt, Cancel );

        var preview = await subject.Records.RecordPreviewImageAsync( video.Id, Image( "preview-1" ), CreatedAt, Cancel );
        var again = await subject.Records.RecordPreviewImageAsync( video.Id, Image( "preview-2" ), CreatedAt, Cancel );

        Assert.NotNull( preview );
        Assert.Null( again );
        Assert.Equal(
            preview.Id,
            (await subject.Records.AttachedBlobAsync( AttachmentOwner.ForBlob( video.Id ), BlobRecords.PreviewImageName, Cancel ))?.Id );
        Assert.False( await subject.HasBlobAsync( "preview-2" ) );
    }

    [Fact]
    public async Task Rows_are_stored_as_rails_stores_them( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var records = new BlobRecords( testDatabase.Database );

        var blob = await records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel );
        var variant = await records.RecordVariantAsync( blob.Id, "digest", Image( "variant-1" ), CreatedAt, Cancel );

        Assert.Equal(
            "2026-09-26 12:34:56.123456",
            await testDatabase.ScalarAsync<string>( "SELECT created_at FROM active_storage_blobs WHERE id = @p0", blob.Id ) );
        Assert.Equal( "{\"identified\":true}", await testDatabase.ScalarAsync<string>( "SELECT metadata FROM active_storage_blobs WHERE id = @p0", blob.Id ) );
        Assert.Equal( "moon.png", await testDatabase.ScalarAsync<string>( "SELECT filename FROM active_storage_blobs WHERE id = @p0", blob.Id ) );
        Assert.Equal(
            "ActiveStorage::VariantRecord image 2026-09-26 12:34:56.123456",
            await testDatabase.ScalarAsync<string>(
                "SELECT record_type || ' ' || name || ' ' || created_at FROM active_storage_attachments WHERE blob_id = @p0",
                variant!.Id ) );
        Assert.Equal(
            "digest",
            await testDatabase.ScalarAsync<string>( "SELECT variation_digest FROM active_storage_variant_records WHERE blob_id = @p0", blob.Id ) );
    }

    [Fact]
    public async Task A_taken_key_is_refused_by_the_unique_index( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var records = new BlobRecords( testDatabase.Database );
        await records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel );

        await Assert.ThrowsAsync<RecordNotUniqueException>( ( ) => records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel ) );

        Assert.Equal( 1, await testDatabase.CountAsync( "active_storage_blobs" ) );
    }

    [Fact]
    public async Task A_lost_variant_race_writes_nothing( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var records = new BlobRecords( testDatabase.Database );
        var blob = await records.InsertBlobAsync( Image( "key-1" ), CreatedAt, Cancel );
        await records.RecordVariantAsync( blob.Id, "digest", Image( "variant-1" ), CreatedAt, Cancel );

        await records.RecordVariantAsync( blob.Id, "digest", Image( "variant-2" ), CreatedAt, Cancel );

        var blobs = await testDatabase.CountAsync( "active_storage_blobs" );
        var attachments = await testDatabase.CountAsync( "active_storage_attachments" );
        var variants = await testDatabase.CountAsync( "active_storage_variant_records" );
        Assert.Equal( (2, 1, 1), (blobs, attachments, variants) );
    }

    /// <summary>An attachment row as <c>has_one_attached</c> inserts it.</summary>
    private static Task<long> InsertAttachmentAsync( TestDatabase testDatabase, string name, AttachmentOwner owner, long blobId ) =>
        testDatabase.WriteAsync( scope => StorageRows.InsertAttachment( scope.Db, new ActiveStorageAttachment
        {
            Name = name,
            RecordType = owner.RecordType,
            RecordId = owner.RecordId,
            BlobId = blobId,
            CreatedAt = CreatedAt.UtcDateTime,
        } ) );

    private static NewBlob Image( string key ) => new( )
    {
        Key = key,
        Filename = new BlobFilename( "moon.png" ),
        ContentType = "image/png",
        Metadata = BlobMetadata.Identified,
        ServiceName = "local",
        ByteSize = 1234,
        Checksum = "abc==",
    };

    /// <summary>One implementation under test, with what it needs disposed.</summary>
    private sealed class Subject( IBlobRecords records, Func<string, Task<bool>> hasBlob, IDisposable? owner ) : IDisposable
    {
        public IBlobRecords Records { get; } = records;

        public static async Task<Subject> CreateAsync( string implementation )
        {
            if ( implementation == InMemory )
            {
                var inMemory = new InMemoryBlobRecords( );
                return new Subject( inMemory, key => Task.FromResult( inMemory.Blobs.Any( blob => blob.Key == key ) ), null );
            }

            var database = await TestDatabase.CreateAsync( fixtures: false );
            return new Subject(
                new BlobRecords( database.Database ),
                async key => await database.CountAsync( "active_storage_blobs", "\"key\" = @p0", key ) > 0,
                database );
        }

        /// <summary>Whether a blob row with <paramref name="key"/> was written.</summary>
        public Task<bool> HasBlobAsync( string key ) => hasBlob( key );

        public void Dispose( ) => owner?.Dispose( );
    }

    /// <summary>Blobs compared by their stored values: the metadata by its JSON, the filename by its raw text.</summary>
    private sealed class BlobComparer : IEqualityComparer<Blob?>
    {
        public static BlobComparer Instance { get; } = new( );

        public bool Equals( Blob? x, Blob? y ) =>
            x is not null && y is not null
            && (x.Id, x.Key, x.Filename.Raw, x.ContentType, x.Metadata.ToJson( ), x.ServiceName, x.ByteSize, x.Checksum, x.CreatedAt)
            == (y.Id, y.Key, y.Filename.Raw, y.ContentType, y.Metadata.ToJson( ), y.ServiceName, y.ByteSize, y.Checksum, y.CreatedAt);

        public int GetHashCode( Blob? obj ) => obj?.Id.GetHashCode( ) ?? 0;
    }
}
