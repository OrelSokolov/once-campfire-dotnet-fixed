using System.Net;

using Campfire.Application.Integrations.Network;
using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary>
/// <c>Opengraph::Fetch</c> (<c>app/models/opengraph/fetch.rb</c>): GET or HEAD against a pinned address, following up to
/// <see cref="MaxRedirects"/> responses (any 3xx is a redirect), each redirect target parsed, required to be http(s),
/// and resolved through the private network guard again. A document must be a 200 <c>text/html</c> of at most
/// <see cref="MaxBodySize"/>, by <c>Content-Length</c> and by what's actually read (after decompression).
/// </summary>
internal static class OpengraphFetch
{
    public const string AllowedDocumentContentType = "text/html";

    public const int MaxBodySize = 5 * 1024 * 1024;

    public const int MaxRedirects = 10;

    /// <summary><c>fetch_document(url, ip:)</c>: the body, or null when the response isn't acceptable.</summary>
    public static async Task<byte[]?> FetchDocumentAsync(
        OpengraphFetchContext fetchContext, RubyUri url, IPAddress ip, CancellationToken cancellationToken )
    {
        using var response = await RequestAsync( fetchContext, url, ip, HttpMethod.Get, cancellationToken );
        if ( response.Status != 200 || response.ContentType != AllowedDocumentContentType || (response.ContentLength( ) ?? 0) > MaxBodySize )
        {
            return null;
        }

        return await response.ReadBodyAsync( MaxBodySize, cancellationToken );
    }

    /// <summary><c>fetch_content_type(url, ip:)</c>: the final response's <c>Content-Type</c> as sent, whatever its status.</summary>
    public static async Task<string?> FetchContentTypeAsync(
        OpengraphFetchContext fetchContext, RubyUri url, IPAddress ip, CancellationToken cancellationToken )
    {
        using var response = await RequestAsync( fetchContext, url, ip, HttpMethod.Head, cancellationToken );
        return response.Header( HttpHeaderNames.ContentType );
    }

    private static async Task<NetHttpResponse> RequestAsync(
        OpengraphFetchContext fetchContext, RubyUri url, IPAddress ip, HttpMethod method, CancellationToken cancellationToken )
    {
        for ( var attempt = 0; attempt < MaxRedirects; attempt++ )
        {
            var response = await SendAsync( fetchContext, url, ip, method, cancellationToken );
            if ( response.Status is < 300 or >= 400 )
            {
                return response;
            }

            using ( response )
            {
                (url, ip) = await ResolveRedirectAsync( fetchContext, response.Header( HttpHeaderNames.Location ), cancellationToken );
            }
        }

        throw new OpengraphFetchException( "Opengraph::Fetch::TooManyRedirectsError" );
    }

    private static async Task<(RubyUri Url, IPAddress Ip)> ResolveRedirectAsync(
        OpengraphFetchContext fetchContext, string? location, CancellationToken cancellationToken )
    {
        if ( location is null || RubyUri.TryParse( location, out var url ) != RubyUriError.None )
        {
            throw new OpengraphFetchException( RubyUri.InvalidUriMessage( location ) );
        }

        if ( !url!.IsHttp )
        {
            throw new OpengraphFetchException( "Opengraph::Fetch::RedirectDeniedError" );
        }

        return (url, await PrivateNetworkGuard.ResolveAsync( fetchContext.Resolver, url.Host, cancellationToken ));
    }

    /// <summary>
    /// <c>Net::HTTP.start(url.host, url.port, ipaddr: ip, use_ssl: url.scheme == "https")</c> and
    /// <c>http.request(request_class.new(url))</c>.
    /// </summary>
    private static Task<NetHttpResponse> SendAsync(
        OpengraphFetchContext fetchContext, RubyUri url, IPAddress ip, HttpMethod method, CancellationToken cancellationToken )
    {
        if ( url.Host is not { Length: > 0 } host || !url.TryGetTcpPort( out var port ) )
        {
            throw new OpengraphFetchException( "no host component for URI" );
        }

        var endpoint = new NetHttpEndpoint( url.IsHttps, host, port, ip );
        var request = NetHttpRequest.Create( method, endpoint, NetHttpRequest.RequestUri( url ), HostHeaderPlacement.AfterUserAgent );
        return SendDisposingAsync( fetchContext, request, cancellationToken );
    }

    private static async Task<NetHttpResponse> SendDisposingAsync(
        OpengraphFetchContext fetchContext, HttpRequestMessage request, CancellationToken cancellationToken )
    {
        using ( request )
        {
            return await NetHttpRequest.SendAsync( fetchContext.Http, request, cancellationToken );
        }
    }
}
