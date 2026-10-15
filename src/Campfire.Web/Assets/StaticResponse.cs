namespace Campfire.Web.Assets;

/// <summary>
/// A static response. Header names are as Rack spells them (<c>Cache-Control</c> comes from the
/// app's configuration); the body is empty for HEAD.
/// </summary>
public sealed record StaticResponse( int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, ReadOnlyMemory<byte> Body )
{
    public string? Header( string name ) =>
        Headers.FirstOrDefault( header => string.Equals( header.Key, name, StringComparison.OrdinalIgnoreCase ) ).Value;
}
