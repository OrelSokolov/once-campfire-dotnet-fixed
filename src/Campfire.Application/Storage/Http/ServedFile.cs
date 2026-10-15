namespace Campfire.Application.Storage.Http;

/// <summary>
/// A response described without an HTTP framework: status, headers in the order Rails sets them,
/// and the body as parts the web layer writes out (streaming file parts rather than buffering them).
/// </summary>
public sealed record ServedFile( int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, IReadOnlyList<BodyPart> Body )
{
    public long BodyLength => Body.Sum( part => part.Length );

    /// <summary>The value of the header named <paramref name="name"/> (case-insensitive), or <c>null</c>.</summary>
    public string? Header( string name ) =>
        Headers.Where( header => string.Equals( header.Key, name, StringComparison.OrdinalIgnoreCase ) ).Select( header => header.Value ).FirstOrDefault( );
}
