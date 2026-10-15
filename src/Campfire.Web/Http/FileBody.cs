namespace Campfire.Web.Http;

/// <summary>A byte range of a file on disk.</summary>
public sealed class FileBody( string path, long offset, long length ) : ResponseBody
{
    public string Path { get; } = path;

    public long Offset { get; } = offset;

    public override long? Length { get; } = length;

    internal override Task WriteAsync( HttpResponse response, CancellationToken cancellationToken ) =>
        response.SendFileAsync( Path, Offset, Length, cancellationToken );
}
