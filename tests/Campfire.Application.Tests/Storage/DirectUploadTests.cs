using System.Text;
using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Storage;
using Campfire.Application.Tests.Storage.Support;
using Campfire.Application.Tests.Support;

namespace Campfire.Application.Tests.Storage;

/// <summary>
/// The direct-upload flow's storage half: <c>create_before_direct_upload!</c>, the disk service's <c>PUT</c>, and the
/// <c>identify_without_saving</c> a blob gets when it is attached by its signed id.
/// </summary>
public sealed class DirectUploadTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_blob_is_created_before_its_bytes_with_empty_metadata_and_the_service_name( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );

        var blob = await testDatabase.WriteAsync( scope => DirectUploads.CreateBlob(
            scope,
            new DirectUploadBlob( "quota.bin", 6, "Bn0IqYNu7+uSlO0ILUMJaQ==", "application/octet-stream", "{}" ),
            "local" ) );

        var stored = await testDatabase.ReadAsync( db => BlobRecords.FindById( db, blob.Id )! );
        Assert.Equal(
            ("quota.bin", 6L, "application/octet-stream", "{}", "local"),
            (stored.Filename.Raw, stored.ByteSize, stored.ContentType, stored.Metadata.ToJson( ), stored.ServiceName) );
        Assert.Equal( BlobKeys.Length, stored.Key.Length );
        Assert.True( BlobKeys.IsWellFormed( stored.Key ) );
        Assert.InRange( stored.CreatedAt, testDatabase.Clock.GetUtcNow( ).AddMinutes( -1 ), testDatabase.Clock.GetUtcNow( ) );
    }

    [Fact]
    public async Task A_blank_checksum_is_invalid_before_the_missing_columns_fail_the_insert( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var blobs = await testDatabase.CountAsync( "active_storage_blobs" );

        var invalid = await Assert.ThrowsAsync<RecordInvalidException>( ( ) => testDatabase.WriteAsync( scope => DirectUploads.CreateBlob(
            scope,
            new DirectUploadBlob( null, null, " ", null, "{}" ),
            "local" ) ) );
        await Assert.ThrowsAsync<NotNullViolationException>( ( ) => testDatabase.WriteAsync( scope => DirectUploads.CreateBlob(
            scope,
            new DirectUploadBlob( null, 6, "abc", null, "{}" ),
            "local" ) ) );
        await Assert.ThrowsAsync<NotNullViolationException>( ( ) => testDatabase.WriteAsync( scope => DirectUploads.CreateBlob(
            scope,
            new DirectUploadBlob( "a.bin", null, "abc", null, "{}" ),
            "local" ) ) );

        Assert.Equal( ["Checksum can't be blank"], invalid.Errors.FullMessages );
        Assert.Equal( blobs, await testDatabase.CountAsync( "active_storage_blobs" ) );
    }

    [Fact]
    public async Task Identifying_reads_the_first_bytes_and_marks_the_metadata_identified_in_place( )
    {
        using var harness = new StorageHarness( );
        var bytes = await File.ReadAllBytesAsync( StorageVectors.Fixture( "moon.jpg" ), Cancel );
        var blob = await PlaceAsync( harness, bytes, "photo.bin", "application/octet-stream", """{"custom":{"a":1},"identified":false}""" );

        var identity = await harness.Storage.IdentifyWithoutSavingAsync( blob, Cancel );

        Assert.Equal( new BlobIdentity( "image/jpeg", """{"custom":{"a":1},"identified":true}""" ), identity );
    }

    [Fact]
    public async Task An_identified_blob_is_left_alone_and_an_empty_one_is_identified_by_its_name_without_a_read( )
    {
        using var harness = new StorageHarness( );
        var identified = await PlaceAsync( harness, [1, 2, 3], "a.bin", "application/octet-stream", """{"identified":true}""" );
        var empty = new Blob
        {
            Id = 7,
            Key = BlobKeys.Generate( ),
            Filename = new BlobFilename( "notes.txt" ),
            ContentType = null,
            ServiceName = "local",
            ByteSize = 0,
            Checksum = "1B2M2Y8AsgTpgAmY7PhCfg==",
        };

        Assert.Null( await harness.Storage.IdentifyWithoutSavingAsync( identified, Cancel ) );
        Assert.Equal( new BlobIdentity( "text/plain", """{"identified":true}""" ), await harness.Storage.IdentifyWithoutSavingAsync( empty, Cancel ) );
    }

    [Fact]
    public async Task Identifying_a_blob_whose_bytes_never_arrived_fails_as_a_missing_file( )
    {
        using var harness = new StorageHarness( );
        var blob = new Blob
        {
            Id = 8,
            Key = BlobKeys.Generate( ),
            Filename = new BlobFilename( "a.bin" ),
            ServiceName = "local",
            ByteSize = 6,
            Checksum = "Bn0IqYNu7+uSlO0ILUMJaQ==",
        };

        var error = await Assert.ThrowsAsync<StorageException>( ( ) => harness.Storage.IdentifyWithoutSavingAsync( blob, Cancel ) );

        Assert.Equal( StorageFailure.FileNotFound, error.Failure );
    }

    [Fact]
    public async Task A_direct_upload_is_stored_only_when_its_bytes_match_the_checksum_and_a_repeat_keeps_the_file( )
    {
        using var harness = new StorageHarness( );
        var key = BlobKeys.Generate( );
        var content = "hello!"u8.ToArray( );
        var checksum = BlobKeys.Checksum( content );

        var mismatch = await Assert.ThrowsAsync<StorageException>( ( ) => harness.Storage.ReceiveDirectUploadAsync(
            key,
            new MemoryStream( "hellO!"u8.ToArray( ) ),
            checksum,
            Cancel ) );
        Assert.Equal( StorageFailure.Integrity, mismatch.Failure );
        Assert.False( harness.Storage.Disk.Exists( key ) );

        await harness.Storage.ReceiveDirectUploadAsync( key, new MemoryStream( content ), checksum, Cancel );
        await harness.Storage.ReceiveDirectUploadAsync( key, new MemoryStream( content ), checksum, Cancel );

        Assert.Equal( "hello!", Encoding.UTF8.GetString( await File.ReadAllBytesAsync( harness.Storage.Disk.PathFor( key ), Cancel ) ) );
        Assert.Equal( [Path.Combine( key[..2], key[2..4], key )], harness.Files );
    }

    [Fact]
    public async Task A_key_that_cannot_name_a_file_is_refused( )
    {
        using var harness = new StorageHarness( );
        var content = "hello!"u8.ToArray( );

        var error = await Assert.ThrowsAsync<StorageException>( ( ) => harness.Storage.ReceiveDirectUploadAsync(
            "../../etc/passwd",
            new MemoryStream( content ),
            BlobKeys.Checksum( content ),
            Cancel ) );

        Assert.Equal( StorageFailure.FileNotFound, error.Failure );
        Assert.Empty( harness.Files );
    }

    private static async Task<Blob> PlaceAsync( StorageHarness harness, byte[] bytes, string filename, string? contentType, string metadata )
    {
        var key = BlobKeys.Generate( );
        var path = harness.Storage.Disk.PathFor( key );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        await File.WriteAllBytesAsync( path, bytes, Cancel );

        return new Blob
        {
            Id = 1,
            Key = key,
            Filename = new BlobFilename( filename ),
            ContentType = contentType,
            Metadata = BlobMetadata.Parse( metadata ),
            ServiceName = "local",
            ByteSize = bytes.Length,
            Checksum = BlobKeys.Checksum( bytes ),
        };
    }
}
