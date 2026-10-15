namespace Campfire.Application.Storage;

/// <summary>A temporary local copy of a blob's file (<c>blob.open</c>); disposing deletes it.</summary>
public sealed class BlobCopy( string path ) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose( ) => File.Delete( Path );
}
