using System.Net;
using System.Net.Sockets;
using Campfire.Application.Ruby;


namespace Campfire.Application.Integrations.Network;

/// <summary>Where <c>Net::HTTP</c> writes the <c>Host</c> header it adds to a request.</summary>
public enum HostHeaderPlacement
{
    /// <summary>A request built from a URI (<c>Get.new(uri)</c>): <c>Host</c> right after <c>User-Agent</c>.</summary>
    AfterUserAgent,

    /// <summary>A request built from a path: <c>Host</c> from <c>addr_port</c>, after <c>Connection</c>.</summary>
    AfterConnection,
}

/// <summary>
/// Builds requests the way <c>Net::HTTP</c> does and sends them: the caller's headers first, then
/// <c>Accept-Encoding</c> (whose gzip or deflate reply the response inflates), <c>Accept</c>, <c>User-Agent</c>,
/// <c>Host</c>, and <c>Connection: close</c> for a connection the request itself starts. Header names and values are
/// exactly <c>Net::HTTP</c>'s; content headers (<c>Content-Type</c>, <c>Content-Encoding</c>, <c>Content-Length</c>)
/// follow the others on the wire, where <c>Net::HTTP</c> writes them in the caller's order (field order carries no
/// meaning in HTTP, RFC 9110 section 5.3).
/// </summary>
public static class NetHttpRequest
{
    /// <summary>The <c>Accept-Encoding</c> <c>Net::HTTP</c> adds and then decodes itself.</summary>
    public const string DefaultAcceptEncoding = "gzip;q=1.0,deflate;q=0.6,identity;q=0.3";

    private static readonly HashSet<string> ContentHeaders = new( StringComparer.OrdinalIgnoreCase )
    {
        HttpHeaderNames.ContentType,
        HttpHeaderNames.ContentEncoding,
        HttpHeaderNames.ContentLength,
    };

    private static readonly HttpRequestOptionsKey<bool> DecodeContentKey = new( "Campfire.NetHttp.DecodeContent" );

    /// <summary>
    /// <c>Net::HTTP::Get.new(uri)</c>, <c>Head.new(uri)</c> or <c>Post.new(uri, headers)</c>, sent on a connection the
    /// request starts itself when <paramref name="connectionClose"/> (<c>Connection: close</c>).
    /// </summary>
    /// <param name="method">GET, HEAD or POST.</param>
    /// <param name="endpoint">Where it goes.</param>
    /// <param name="requestUri"><c>URI#request_uri</c>: the path (at least "/") and query.</param>
    /// <param name="hostPlacement">Whether the request was built from a URI or a path, which places <c>Host</c>.</param>
    /// <param name="connectionClose">Whether <c>Net::HTTP#request</c> starts the connection (and so asks to close it).</param>
    /// <param name="headers">The caller's headers, in order (none when null).</param>
    /// <param name="body">The request body, if any.</param>
    public static HttpRequestMessage Create(
        HttpMethod method,
        NetHttpEndpoint endpoint,
        string requestUri,
        HostHeaderPlacement hostPlacement,
        bool connectionClose = false,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null,
        byte[]? body = null )
    {
        ArgumentNullException.ThrowIfNull( method );
        ArgumentNullException.ThrowIfNull( endpoint );

        headers ??= [];

        var request = new HttpRequestMessage( method, TargetUri( endpoint, requestUri ) )
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        if ( body is not null )
        {
            request.Content = new ByteArrayContent( body );
        }

        foreach ( var (name, value) in headers )
        {
            Add( request, name, value );
        }

        var decode = false;
        var callerNegotiatesEncoding = headers.Any(
            static header => IsHeader( header.Key, HttpHeaderNames.AcceptEncoding ) || IsHeader( header.Key, HttpHeaderNames.Range ) );
        if ( !callerNegotiatesEncoding )
        {
            decode = method != HttpMethod.Head;
            Add( request, HttpHeaderNames.AcceptEncoding, DefaultAcceptEncoding );
        }

        Default( request, HttpHeaderNames.Accept, "*/*" );
        Default( request, HttpHeaderNames.UserAgent, "Ruby" );
        if ( hostPlacement == HostHeaderPlacement.AfterUserAgent )
        {
            Default( request, HttpHeaderNames.Host, endpoint.HostHeader );
        }

        if ( connectionClose )
        {
            request.Headers.ConnectionClose = true;
        }

        if ( hostPlacement == HostHeaderPlacement.AfterConnection )
        {
            Default( request, HttpHeaderNames.Host, endpoint.HostHeader );
        }

        request.Options.Set( DecodeContentKey, decode );
        return request;
    }

    /// <summary>
    /// Sends <paramref name="request"/> and returns once the response head has arrived. Failures come back as
    /// <see cref="NetHttpException"/>, except cancellation through <paramref name="cancellationToken"/>.
    /// </summary>
    public static async Task<NetHttpResponse> SendAsync( HttpMessageInvoker http, HttpRequestMessage request, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( http );
        ArgumentNullException.ThrowIfNull( request );

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync( request, cancellationToken );
        }
        catch ( Exception error ) when ( error is not OperationCanceledException || !cancellationToken.IsCancellationRequested )
        {
            throw NetHttpException.From( error );
        }

        var decode = request.Options.TryGetValue( DecodeContentKey, out var value ) && value;
        return new NetHttpResponse( response, decode, request.Method == HttpMethod.Head );
    }

    /// <summary><c>URI::HTTP#request_uri</c>: the path (at least "/") and the query.</summary>
    internal static string RequestUri( RubyUri uri )
    {
        var target = uri.Query is null ? uri.Path ?? "" : $"{uri.Path}?{uri.Query}";
        return target.StartsWith( '/' ) ? target : "/" + target;
    }

    /// <summary>The URI the handler connects to: the pinned address when there is one, and the path exactly as given.</summary>
    private static Uri TargetUri( NetHttpEndpoint endpoint, string requestUri )
    {
        var host = endpoint.PinnedAddress switch
        {
            null => endpoint.Host,
            { AddressFamily: AddressFamily.InterNetworkV6 } address => $"[{address}]",
            { } address => address.ToString( ),
        };
        var scheme = endpoint.Https ? "https" : "http";
        var options = new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true };
        if ( !Uri.TryCreate( $"{scheme}://{host}:{endpoint.Port}{requestUri}", options, out var uri ) )
        {
            throw new NetHttpException( NetHttpFailure.InvalidRequest, RubyUri.InvalidUriMessage( $"{scheme}://{endpoint.Host}{requestUri}" ) );
        }

        return uri;
    }

    private static void Add( HttpRequestMessage request, string name, string value )
    {
        if ( ContentHeaders.Contains( name ) )
        {
            if ( request.Content is { } content && !IsHeader( name, HttpHeaderNames.ContentLength ) )
            {
                content.Headers.TryAddWithoutValidation( name, value );
            }

            return;
        }

        if ( IsHeader( name, HttpHeaderNames.Connection ) && value.Equals( "close", StringComparison.OrdinalIgnoreCase ) )
        {
            request.Headers.ConnectionClose = true;
            return;
        }

        request.Headers.TryAddWithoutValidation( name, value );
    }

    /// <summary>Header names compare ignoring case.</summary>
    private static bool IsHeader( string name, string headerName ) => name.Equals( headerName, StringComparison.OrdinalIgnoreCase );

    private static void Default( HttpRequestMessage request, string name, string value )
    {
        if ( !request.Headers.NonValidated.Contains( name ) )
        {
            request.Headers.TryAddWithoutValidation( name, value );
        }
    }
}
