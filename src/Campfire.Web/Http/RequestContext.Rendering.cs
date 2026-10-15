using System.Buffers;
using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary><c>render</c>, <c>head</c>, <c>redirect_to</c> and URL helpers (rails <c>1a02651</c>).</summary>
public sealed partial class RequestContext
{
    /// <summary>The characters of a URI scheme after its first letter.</summary>
    private static readonly SearchValues<char> SchemeCharacters =
        SearchValues.Create( "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+-." );

    /// <summary>
    /// A rendered template, labelled with <em>the template's</em> format: Rails sets
    /// <c>rendered_format</c> from the template it found, so an <c>.html.erb</c>-only action answers
    /// <c>text/html</c> even when <c>Accept</c> prefers Turbo Streams.
    /// </summary>
    public FrontendResult Render( int status, MimeType template, ResponseBody body )
    {
        ArgumentNullException.ThrowIfNull( template );

        _renderedFormat = template;
        return RenderAs( status, $"{template.Value}; charset=utf-8", body );
    }

    public FrontendResult Render( int status, MimeType template, string body ) => Render( status, template, ResponseBody.FromString( body ) );

    /// <summary><c>render</c> with an explicit content type (<c>render json:</c>, <c>send_data</c>, ...), with the <c>Vary</c> rule.</summary>
    public FrontendResult RenderAs( int status, string contentType, ResponseBody body ) =>
        SetVaryHeader( FrontendResult.WithBody( status, contentType, body ) );

    /// <summary><c>render html:</c> / an HTML template, as <c>text/html; charset=utf-8</c>.</summary>
    public FrontendResult Html( string html, int status = StatusCodes.Status200OK ) =>
        RenderAs( status, ContentTypes.HtmlUtf8, ResponseBody.FromString( html ) );

    /// <summary><c>render turbo_stream:</c>.</summary>
    public FrontendResult TurboStream( string html, int status = StatusCodes.Status200OK )
    {
        _renderedFormat = Mime.TurboStream;
        return RenderAs( status, ContentTypes.TurboStreamUtf8, ResponseBody.FromString( html ) );
    }

    /// <summary>
    /// <c>render json:</c>: ActiveSupport's encoder, which with <c>escape_json_responses = false</c>
    /// (8.1 defaults) escapes nothing.
    /// </summary>
    public FrontendResult Json( JsonNode? value, int status = StatusCodes.Status200OK ) =>
        RenderAs( status, ContentTypes.JsonUtf8, ResponseBody.FromBytes( Encoding.UTF8.GetBytes( JsonPayload.Generate( value ) ) ) );

    /// <summary>
    /// <c>head status</c>: no body; a bare content type (no charset) unless the status has no content.
    /// Before the action proper starts (in a before-action) Rails hasn't assigned the controller's
    /// formats yet, so the type is <c>text/html</c> whatever the request format.
    /// </summary>
    public FrontendResult Head( int status )
    {
        var response = new FrontendResult( status );
        if ( !IsNoContentStatus( status ) )
        {
            response.Headers.ContentType = RenderedFormat( ).Value;
        }

        return response;
    }

    /// <summary><c>head status, location: url</c>.</summary>
    public FrontendResult HeadWithLocation( int status, string location )
    {
        var resolved = ComputeLocation( location );

        return Head( status ).Header( HeaderNames.Location, resolved );
    }

    /// <summary>
    /// <c>redirect_to location, status:, notice:, alert:, allow_other_host:</c>. Paths get the
    /// request's protocol and host; a path-relative URL raises (<c>action_on_path_relative_redirect =
    /// :raise</c>); another host raises unless allowed (<c>raise_on_open_redirects</c>).
    /// </summary>
    public FrontendResult RedirectTo( string location, RedirectOptions? options = null )
    {
        options ??= new RedirectOptions( );
        if ( options.Notice is not null )
        {
            Flash.Notice = options.Notice;
        }

        if ( options.Alert is not null )
        {
            Flash.Alert = options.Alert;
        }

        var resolved = ComputeLocation( location );
        if ( resolved.Any( character => character < 0x20 && character != '\t' ) )
        {
            throw new UnsafeRedirectException( $"The redirect URL {resolved} contains illegal characters" );
        }

        if ( !options.AllowOtherHost && !UrlHostAllowed( resolved ) )
        {
            throw new UnsafeRedirectException( $"Unsafe redirect to \"{resolved}\", pass allow_other_host: true to redirect anyway." );
        }

        var response = new FrontendResult( options.Status ).Header( HeaderNames.Location, resolved );
        response.Headers.ContentType = ContentTypes.HtmlUtf8;

        return response;
    }

    /// <summary><c>redirect_back_or_to fallback</c>: the referer when it's on this host.</summary>
    public FrontendResult RedirectBackOrTo( string fallback, RedirectOptions? options = null )
    {
        var referer = Request.Referer;

        return referer is not null && UrlHostAllowed( referer ) ? RedirectTo( referer, options ) : RedirectTo( fallback, options );
    }

    /// <summary>
    /// An absolute URL for <paramref name="path"/> on this request's host: what the <c>*_url</c> helpers
    /// produce with the <c>default_url_options</c> <c>SetCurrentRequest</c> provides.
    /// </summary>
    public string UrlFor( string path ) => $"{Request.Protocol}{Request.HostWithPort}{path}";

    internal static bool IsNoContentStatus( int status ) => status is (>= 100 and < 200) or 204 or 205 or 304;

    /// <summary>
    /// <c>_compute_redirect_to_location</c> for strings: absolute URLs pass through, paths get the
    /// request's protocol and host, and a path-relative URL raises.
    /// </summary>
    private string ComputeLocation( string location )
    {
        ArgumentNullException.ThrowIfNull( location );

        string url;
        if ( IsAbsolute( location ) )
        {
            url = location;
        }
        else
        {
            if ( location.Length > 0 && location[0] is not ('/' or '?') )
            {
                throw new UnsafeRedirectException( $"Path relative URL redirect detected: \"{location}\"" );
            }

            url = $"{Request.Protocol}{Request.HostWithPort}{location}";
        }

        return url.Replace( "\0", "", StringComparison.Ordinal ).Replace( "\r", "", StringComparison.Ordinal ).Replace( "\n", "", StringComparison.Ordinal );
    }

    /// <summary><c>/\A([a-z][a-z\d\-+.]*:|\/\/).*/i</c>.</summary>
    private static bool IsAbsolute( string location )
    {
        if ( location.StartsWith( "//", StringComparison.Ordinal ) )
        {
            return true;
        }

        var colon = location.IndexOf( ':', StringComparison.Ordinal );

        return colon > 0 && char.IsAsciiLetter( location[0] )
            && !location.AsSpan( 0, colon ).ContainsAnyExcept( SchemeCharacters );
    }

    /// <summary>
    /// <c>_url_host_allowed?</c>: <c>URI(url).host == request.host</c> (compared exactly, as Ruby does),
    /// or a plain path.
    /// </summary>
    private bool UrlHostAllowed( string url )
    {
        var host = UrlHost( url );

        return host is not null ? host == Request.Host : url.StartsWith( '/' ) && !url.StartsWith( "//", StringComparison.Ordinal );
    }

    /// <summary>The host of an absolute or protocol-relative URL; <c>null</c> for paths.</summary>
    private static string? UrlHost( string url )
    {
        string rest;
        if ( url.StartsWith( "//", StringComparison.Ordinal ) )
        {
            rest = url[2..];
        }
        else
        {
            var separator = url.IndexOf( "://", StringComparison.Ordinal );
            if ( separator <= 0 || url.AsSpan( 0, separator ).ContainsAnyExcept( SchemeCharacters ) )
            {
                return null;
            }

            rest = url[(separator + 3)..];
        }

        var end = rest.IndexOfAny( ['/', '?', '#'] );
        var authority = end < 0 ? rest : rest[..end];
        var hostPort = authority[(authority.LastIndexOf( '@' ) + 1)..];
        if ( hostPort.StartsWith( '[' ) )
        {
            var close = hostPort.IndexOf( ']', StringComparison.Ordinal );
            return close < 0 ? hostPort : hostPort[..(close + 1)];
        }

        var colon = hostPort.IndexOf( ':', StringComparison.Ordinal );

        return colon < 0 ? hostPort : hostPort[..colon];
    }
}
