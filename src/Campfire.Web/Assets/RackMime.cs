namespace Campfire.Web.Assets;

/// <summary><c>Rack::Mime</c>: content types by file extension, as <c>Rack::Files</c> and <c>ActionDispatch::Static</c> use them.</summary>
internal static partial class RackMime
{
    /// <summary><c>Rack::Mime.mime_type(ext, nil)</c>: the type for <c>.ext</c> (case-insensitive), or <c>null</c>.</summary>
    public static string? Lookup( string extension ) =>
        MimeTypes.TryGetValue( extension.ToLowerInvariant( ), out var type ) ? type : null;
}
