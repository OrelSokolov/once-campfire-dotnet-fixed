using System.Text;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Disk;

namespace Campfire.Application.Tests.Storage;

/// <summary>The disk layout, staged writes published by rename, checksum verification and cleanup.</summary>
public sealed class DiskServiceTests : IDisposable
{
    private readonly string _root = Path.Combine( Path.GetTempPath( ), "campfire-disk-tests-" + Guid.NewGuid( ).ToString( "N" ) );
    private readonly DiskService _disk;

    public DiskServiceTests( )
    {
        _disk = new DiskService( _root );
    }

    public void Dispose( )
    {
        if ( Directory.Exists( _root ) )
        {
            Directory.Delete( _root, recursive: true );
        }
    }

    [Fact]
    public void Keys_map_to_two_folder_levels_under_the_root( )
    {
        Assert.Equal( Path.Combine( _root, "1y", "kr", "1ykrknnn9r4youv0f71wrntkzilx" ), _disk.PathFor( "1ykrknnn9r4youv0f71wrntkzilx" ) );
        Assert.Equal( "local", _disk.Name );
    }

    [Theory]
    [InlineData( "../../../etc/passwd" )]
    [InlineData( "ab/cd" )]
    [InlineData( ".staging" )]
    [InlineData( "" )]
    [InlineData( "ab\0cd" )]
    public void Keys_that_are_not_alphanumeric_never_become_paths( string key )
    {
        Assert.Throws<ArgumentException>( ( ) => _disk.PathFor( key ) );
    }

    [Fact]
    public void Generated_keys_are_28_base36_characters( )
    {
        var keys = Enumerable.Range( 0, 200 ).Select( _ => BlobKeys.Generate( ) ).ToList( );

        Assert.All( keys, key => Assert.Matches( "^[0-9a-z]{28}$", key ) );
        Assert.Equal( keys.Count, keys.Distinct( ).Count( ) );
    }

    [Fact]
    public async Task Staged_files_are_invisible_until_published_and_then_whole( )
    {
        var key = BlobKeys.Generate( );
        using var staged = await _disk.StageAsync( Stream( "hello world" ), 1024, TestContext.Current.CancellationToken );

        Assert.False( _disk.Exists( key ) );
        Assert.Equal( 11, staged.Length );
        Assert.Equal( "XrY7u+Ae7tCTyyK7j1rNww==", staged.Checksum );
        Assert.Equal( "hello world", Encoding.ASCII.GetString( staged.Head.Span ) );
        Assert.StartsWith( _disk.StagingDirectory, staged.Path, StringComparison.Ordinal );

        _disk.Publish( staged, key );

        Assert.True( _disk.Exists( key ) );
        Assert.Equal( "hello world", await File.ReadAllTextAsync( _disk.PathFor( key ), TestContext.Current.CancellationToken ) );
        Assert.Empty( Directory.EnumerateFiles( _disk.StagingDirectory ) );
    }

    [Fact]
    public async Task Publishing_never_replaces_an_existing_file( )
    {
        var key = BlobKeys.Generate( );
        using ( var first = await _disk.StageAsync( Stream( "first" ), 1024, TestContext.Current.CancellationToken ) )
        {
            _disk.Publish( first, key );
        }

        using var second = await _disk.StageAsync( Stream( "second" ), 1024, TestContext.Current.CancellationToken );

        Assert.Throws<IOException>( ( ) => _disk.Publish( second, key ) );
        Assert.Equal( "first", await File.ReadAllTextAsync( _disk.PathFor( key ), TestContext.Current.CancellationToken ) );
    }

    [Fact]
    public async Task An_unpublished_staged_file_is_deleted_on_dispose( )
    {
        var staged = await _disk.StageAsync( Stream( "abandoned" ), 1024, TestContext.Current.CancellationToken );
        var path = staged.Path;

        staged.Dispose( );

        Assert.False( File.Exists( path ) );
    }

    [Fact]
    public async Task Staging_past_the_limit_fails_and_leaves_nothing( )
    {
        var error = await Assert.ThrowsAsync<StorageException>( ( ) => _disk.StageAsync(
            Stream( new string( 'x', 2000 ) ),
            1024,
            TestContext.Current.CancellationToken ) );

        Assert.Equal( StorageFailure.LimitExceeded, error.Failure );
        Assert.Empty( Directory.EnumerateFiles( _disk.StagingDirectory ) );
    }

    [Fact]
    public async Task Uploads_with_a_wrong_checksum_are_refused_before_they_are_visible( )
    {
        var key = BlobKeys.Generate( );

        var error = await Assert.ThrowsAsync<StorageException>( ( ) => _disk.UploadAsync(
            key,
            Stream( "hello world" ),
            "abc==",
            1024,
            TestContext.Current.CancellationToken ) );
        await _disk.UploadAsync( key, Stream( "hello world" ), "XrY7u+Ae7tCTyyK7j1rNww==", 1024, TestContext.Current.CancellationToken );

        Assert.Equal( StorageFailure.Integrity, error.Failure );
        Assert.True( _disk.Exists( key ) );
        Assert.Empty( Directory.EnumerateFiles( _disk.StagingDirectory ) );
    }

    [Fact]
    public async Task Downloads_and_chunks_read_the_published_file( )
    {
        var key = BlobKeys.Generate( );
        await _disk.UploadAsync( key, Stream( "0123456789" ), null, 1024, TestContext.Current.CancellationToken );

        Assert.Equal( "0123456789", Encoding.ASCII.GetString( await _disk.DownloadAsync( key, TestContext.Current.CancellationToken ) ) );
        Assert.Equal( "345", Encoding.ASCII.GetString( await _disk.DownloadChunkAsync( key, 3, 3, TestContext.Current.CancellationToken ) ) );
        Assert.Equal( "89", Encoding.ASCII.GetString( await _disk.DownloadChunkAsync( key, 8, 5, TestContext.Current.CancellationToken ) ) );

        var missing = await Assert.ThrowsAsync<StorageException>( ( ) => _disk.DownloadAsync( BlobKeys.Generate( ), TestContext.Current.CancellationToken ) );
        Assert.Equal( StorageFailure.FileNotFound, missing.Failure );
    }

    [Fact]
    public async Task Deleting_is_idempotent_and_legacy_variants_go_too( )
    {
        var key = BlobKeys.Generate( );
        await _disk.UploadAsync( key, Stream( "x" ), null, 1024, TestContext.Current.CancellationToken );
        var legacy = Path.Combine( _root, "va", "ri", "variants", key );
        Directory.CreateDirectory( Path.Combine( legacy, "digest" ) );
        await File.WriteAllTextAsync( Path.Combine( legacy, "digest", "file" ), "v", TestContext.Current.CancellationToken );
        await File.WriteAllTextAsync( Path.Combine( legacy, ".hidden" ), "h", TestContext.Current.CancellationToken );

        _disk.Delete( key );
        _disk.Delete( key );
        _disk.DeleteLegacyVariants( key );

        Assert.False( _disk.Exists( key ) );
        Assert.Equal( [Path.Combine( legacy, ".hidden" )], Directory.EnumerateFileSystemEntries( legacy ) );
    }

    [Fact]
    public async Task Sweeping_removes_only_stale_staging_files( )
    {
        var stale = await _disk.StageAsync( Stream( "stale" ), 1024, TestContext.Current.CancellationToken );
        var fresh = await _disk.StageAsync( Stream( "fresh" ), 1024, TestContext.Current.CancellationToken );
        File.SetLastWriteTimeUtc( stale.Path, DateTime.UtcNow.AddHours( -2 ) );

        var removed = _disk.SweepStaging( DateTimeOffset.UtcNow.AddHours( -1 ) );

        Assert.Equal( 1, removed );
        Assert.False( File.Exists( stale.Path ) );
        Assert.True( File.Exists( fresh.Path ) );
        fresh.Dispose( );
    }

    [Fact]
    public async Task Only_files_in_the_staging_directory_can_be_adopted( )
    {
        var outside = Path.Combine( _root, "outside" );

        await Assert.ThrowsAsync<ArgumentException>( ( ) => _disk.AdoptStagedAsync( outside, 10, TestContext.Current.CancellationToken ) );
        await Assert.ThrowsAsync<ArgumentException>( ( ) => _disk.AdoptStagedAsync(
            Path.Combine( _disk.StagingDirectory, "..", "x" ),
            10,
            TestContext.Current.CancellationToken ) );
    }

    private static MemoryStream Stream( string text ) => new( Encoding.ASCII.GetBytes( text ) );
}
