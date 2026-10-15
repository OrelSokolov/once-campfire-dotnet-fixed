namespace Campfire.Application.Storage.Http;

/// <summary>The request fields <c>Rack::Files#serving</c> reads.</summary>
public sealed record FileRequest( string Method, string? Range = null, string? IfModifiedSince = null );
