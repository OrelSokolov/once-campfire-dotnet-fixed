using System.Net;
using System.Net.Http.Headers;

namespace Campfire.IntegrationTests.Support;

/// <summary>What came back over HTTP.</summary>
public sealed record SliceReply( HttpStatusCode Status, HttpResponseHeaders Headers, HttpContentHeaders ContentHeaders, string Body )
{
    /// <summary>The body's bytes as they came over the wire (not decoded).</summary>
    public byte[] WireBytes { get; init; } = [];

    public string? Location => Headers.Location?.ToString( );

    public string? ContentType => ContentHeaders.ContentType?.ToString( );

    /// <summary>A response header as the server wrote it (not re-serialized by <see cref="HttpClient"/>).</summary>
    public string? Raw( string name )
    {
        var found = Headers.NonValidated.TryGetValues( name, out var values ) || ContentHeaders.NonValidated.TryGetValues( name, out values );
        return found ? string.Join( ", ", values ) : null;
    }
}
