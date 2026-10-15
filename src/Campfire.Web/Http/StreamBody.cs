namespace Campfire.Web.Http;

/// <summary>A body produced by a callback while the response streams.</summary>
public sealed class StreamBody( Func<Stream, CancellationToken, Task> writer, long? length ) : ResponseBody
{
    public override long? Length { get; } = length;

    internal override Task WriteAsync( HttpResponse response, CancellationToken cancellationToken ) => writer( response.Body, cancellationToken );
}
