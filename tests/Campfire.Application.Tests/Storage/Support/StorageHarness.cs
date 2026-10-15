using Campfire.Application.Storage;

namespace Campfire.Application.Tests.Storage.Support;

/// <summary>A <see cref="BlobStorage"/> over a fresh temporary <c>storage/files</c>, a private temp directory and in-memory rows.</summary>
internal sealed class StorageHarness : IDisposable
{
    private readonly string _directory;

    public StorageHarness( Action<StorageOptions>? configure = null )
    {
        _directory = Path.Combine( Path.GetTempPath( ), "campfire-storage-tests-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( Path.Combine( _directory, "tmp" ) );
        Options = new StorageOptions
        {
            Root = Path.Combine( _directory, "files" ),
            TempDirectory = Path.Combine( _directory, "tmp" ),
        };
        configure?.Invoke( Options );
        Records = new InMemoryBlobRecords( );
        Storage = new BlobStorage( Options, StorageVectors.Verifier, Records, new FixedTime( StorageVectors.Now ) );
    }

    public StorageOptions Options { get; }

    public InMemoryBlobRecords Records { get; }

    public BlobStorage Storage { get; }

    /// <summary>Every file under the root, staging included, relative to the root.</summary>
    public IReadOnlyList<string> Files =>
        Directory.Exists( Options.Root )
            ? [.. Directory.EnumerateFiles(
                Options.Root,
                "*",
                SearchOption.AllDirectories ).Select( path => Path.GetRelativePath( Options.Root, path ) ).Order( StringComparer.Ordinal )]
            : [];

    /// <summary>Files left in the temp directory (copies that should have been deleted).</summary>
    public IReadOnlyList<string> TempFiles => [.. Directory.EnumerateFiles( Options.TempDirectory! )];

    /// <summary>Uploads a Rails fixture as a message attachment does.</summary>
    public async Task<Blob> UploadFixtureAsync( string fixture, string? declaredType, string? filename = null )
    {
        var stream = File.OpenRead( StorageVectors.Fixture( fixture ) );
        await using ( stream )
        {
            return await Storage.CreateAndUploadAsync( stream, new BlobFilename( filename ?? fixture ), declaredType, TestContext.Current.CancellationToken );
        }
    }

    /// <summary>
    /// The blob with the metadata <c>blob.analyze</c> saves: the file half of <see cref="BlobOperations.AnalyzeAsync"/>
    /// (<see cref="BlobStorage.AnalyzedMetadataAsync"/>), as the in-memory rows have no transaction to save it in.
    /// </summary>
    public async Task<Blob> AnalyzedAsync( Blob blob ) => blob with
    {
        Metadata = await Storage.AnalyzedMetadataAsync( blob, TestContext.Current.CancellationToken )
    };

    public void Dispose( )
    {
        Storage.Dispose( );
        Directory.Delete( _directory, recursive: true );
    }

    private sealed class FixedTime( DateTimeOffset now ) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow( ) => now;
    }
}
