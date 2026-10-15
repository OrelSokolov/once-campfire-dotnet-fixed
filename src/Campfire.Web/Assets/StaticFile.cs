namespace Campfire.Web.Assets;

/// <summary>A file <c>ActionDispatch::Static</c> can serve, with its <c>Last-Modified</c> value.</summary>
internal sealed record StaticFile( ReadOnlyMemory<byte> Content, string LastModified );
