using Campfire.Web.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace Campfire.Web.Cable;

/// <summary>The HTTP side of <c>Connection::Base#process</c>: which requests become sockets.</summary>
internal static class CableRequests
{
    private static readonly string[] AllowedSchemes = [Uri.UriSchemeHttps, Uri.UriSchemeHttp, Uri.UriSchemeWss, Uri.UriSchemeWs];

    /// <summary>
    /// <c>WebSocket::Driver.websocket?(env)</c>: a GET whose <c>Connection</c> lists <c>upgrade</c>
    /// and whose <c>Upgrade</c> is <c>websocket</c> (both case-insensitive).
    /// </summary>
    public static bool IsWebSocketRequest( HttpRequest request )
    {
        // RFC 8441 uses extended CONNECT instead of HTTP/1 Upgrade headers. Kestrel owns
        // the transport validation; CableServer also checks the WebSocket handshake feature.
        if ( HttpMethodChecks.IsConnect( request.Method ) )
        {
            return request.HttpContext.Features.Get<IHttpExtendedConnectFeature>( ) is { IsExtendedConnect: true } extended
                && string.Equals( extended.Protocol, CableProtocol.WebSocketProtocol, StringComparison.OrdinalIgnoreCase );
        }

        if ( !HttpMethodChecks.IsGet( request.Method ) )
        {
            return false;
        }

        var connection = request.Headers.Connection.ToString( ).Split( ',' ).Select( token => token.Trim( ' ' ) );

        return connection.Any( token => token.Equals( CableProtocol.UpgradeToken, StringComparison.OrdinalIgnoreCase ) )
            && request.Headers.Upgrade.ToString( ).Equals( CableProtocol.WebSocketProtocol, StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>
    /// <c>Connection::Base#allow_request_origin?</c>: the same origin as the <c>Host</c> header (with
    /// the scheme <c>Rack::Request#ssl?</c> sees), or one of the configured origins.
    /// </summary>
    public static bool AllowRequestOrigin( HttpRequest request, CableOptions options, ILogger logger )
    {
        if ( options.DisableRequestForgeryProtection )
        {
            return true;
        }

        var origin = request.Headers.Origin.Count > 0 ? request.Headers.Origin.ToString( ) : null;
        var scheme = options.AssumeSsl || IsSsl( request ) ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        if ( options.AllowSameOriginAsHost && origin == $"{scheme}://{request.Headers.Host}" )
        {
            return true;
        }

        if ( origin is not null && options.AllowedRequestOrigins.Contains( origin ) )
        {
            return true;
        }

        CableLog.OriginNotAllowed( logger, origin );

        return false;
    }

    /// <summary>The first protocol in the client's list that Action Cable supports, or <c>null</c> for none.</summary>
    public static string? NegotiateProtocol( IEnumerable<string> requested ) =>
        requested.Select( protocol => protocol.Trim( ) ).FirstOrDefault( protocol => CableProtocol.Protocols.Contains( protocol ) );

    /// <summary><c>Connection::Base#respond_to_invalid_request</c>.</summary>
    public static async Task RespondPageNotFoundAsync( HttpContext httpContext )
    {
        const string Body = "Page not found";

        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        httpContext.Response.ContentType = ContentTypes.PlainTextUtf8;
        httpContext.Response.ContentLength = Body.Length;
        if ( !HttpMethodChecks.IsHead( httpContext.Request.Method ) )
        {
            await httpContext.Response.WriteAsync( Body, httpContext.RequestAborted );
        }
    }

    /// <summary>
    /// <c>Rack::Request#ssl?</c> for a request that isn't TLS itself: <c>X-Forwarded-Ssl: on</c>,
    /// then <c>Forwarded: proto=</c> (last), then <c>X-Forwarded-Proto</c> and
    /// <c>X-Forwarded-Scheme</c> (last allowed value), as rack 3.2 orders them.
    /// </summary>
    private static bool IsSsl( HttpRequest request )
    {
        var scheme = request.IsHttps ? Uri.UriSchemeHttps : ForwardedScheme( request.Headers ) ?? request.Scheme;

        return scheme == Uri.UriSchemeHttps || scheme == Uri.UriSchemeWss;
    }

    private static string? ForwardedScheme( IHeaderDictionary headers )
    {
        if ( headers[ForwardedHeaderNames.Ssl].ToString( ) == "on" )
        {
            return Uri.UriSchemeHttps;
        }

        var forwarded = headers[ForwardedHeaderNames.Forwarded].ToString( )
            .Split( [',', ';'] )
            .Select( pair => pair.Trim( ) )
            .Where( pair => pair.StartsWith( "proto=", StringComparison.OrdinalIgnoreCase ) )
            .Select( pair => pair["proto=".Length..].Trim( '"' ) )
            .LastOrDefault( );
        if ( forwarded is not null && AllowedSchemes.Contains( forwarded ) )
        {
            return forwarded;
        }

        foreach ( var header in new[] { ForwardedHeaderNames.Proto, ForwardedHeaderNames.Scheme } )
        {
            var allowed = SplitHeader( headers[header] ).Reverse( ).FirstOrDefault( AllowedSchemes.Contains );
            if ( allowed is not null )
            {
                return allowed;
            }
        }

        return null;
    }

    /// <summary><c>value.strip.split(/[,\s]+/)</c>.</summary>
    private static string[] SplitHeader( StringValues values ) =>
        values.ToString( ).Split( [',', ' ', '\t', '\n', '\r', '\f', '\v'], StringSplitOptions.RemoveEmptyEntries );
}
