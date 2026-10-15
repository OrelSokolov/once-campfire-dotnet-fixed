using Campfire.Web.Security;

namespace Campfire.Web.Http;

/// <summary>Bot text is literal bytes even when curl labels it as a URL-encoded form.</summary>
public static class BotRequestBody
{
    public const string RoomsSegment = "rooms";
    public const string MessagesSegment = "messages";

    /// <summary>An authenticated bot's request whose body is read raw: anything but a multipart form.</summary>
    public static bool HasRawBody( HttpContext httpContext ) =>
        httpContext.Features.Get<AuthenticatedBot>( ) is not null && IsNonMultipart( httpContext.Request );

    public static bool HasRawBodyPath( PathString path )
    {
        // This only bypasses form method overrides. The selected endpoint still authenticates the key.
        var segments = path.Value?.Split( '/', StringSplitOptions.RemoveEmptyEntries );
        return segments is { Length: >= 4 }
            && segments[0].Equals( RoomsSegment, StringComparison.OrdinalIgnoreCase )
            && long.TryParse( segments[1], out _ )
            && segments[3].Equals( MessagesSegment, StringComparison.OrdinalIgnoreCase );
    }

    private static bool IsNonMultipart( HttpRequest request ) =>
        request.ContentType?.StartsWith( ContentTypes.MultipartForm, StringComparison.OrdinalIgnoreCase ) != true;
}
