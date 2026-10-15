using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>Rendering URLs and headers after the platform has applied the configured proxy trust boundary.</summary>
public sealed class FrontendRequest
{
    private readonly HttpContext _http;

    internal FrontendRequest( HttpContext httpContext )
    {
        _http = httpContext;
    }

    public string Method => _http.Request.Method;
    public string OriginalMethod => _http.Features.Get<IMethodOverrideFeature>( )?.OriginalMethod ?? Method;
    public string Path => _http.Request.PathBase.Add( _http.Request.Path ).ToUriComponent( );
    public string QueryString => _http.Request.QueryString.HasValue ? _http.Request.QueryString.Value![1..] : "";
    public bool IsSsl => _http.Request.IsHttps;
    public IHeaderDictionary Headers => _http.Request.Headers;
    public bool IsGet => HttpMethodChecks.IsGet( Method );
    public bool IsHead => HttpMethodChecks.IsHead( Method );
    public string FullPath => Path + _http.Request.QueryString;
    public string Protocol => _http.Request.Scheme + "://";
    public string Host => _http.Request.Host.Host;
    public int Port => _http.Request.Host.Port ?? (IsSsl ? HttpPorts.Https : HttpPorts.Http);
    public string HostWithPort => _http.Request.Host.Value ?? "";
    public string BaseUrl => Protocol + HostWithPort;
    public string Url => BaseUrl + FullPath;

    /// <summary><c>xml_http_request?</c>: <c>X-Requested-With</c> (every value, as Rack joins them) mentions <c>XMLHttpRequest</c>.</summary>
    public bool IsXhr => IsXmlHttpRequest( Headers );

    public string? Origin => Header( HeaderNames.Origin );
    public string? Referer => Header( HeaderNames.Referer );
    public string? UserAgent => Header( HeaderNames.UserAgent );
    public string? ContentType => _http.Request.ContentType;
    public string? PeerAddress => _http.Connection.RemoteIpAddress?.ToString( );
    public string RemoteIp => PeerAddress ?? "";
    public string? Header( string name ) => Headers.TryGetValue( name, out var values ) && values.Count > 0 ? values[0] : null;

    /// <summary><see cref="IsXhr"/> for a request that has no <see cref="FrontendRequest"/>.</summary>
    internal static bool IsXmlHttpRequest( IHeaderDictionary headers ) =>
        headers[HeaderNames.XRequestedWith].ToString( ).Contains( "XMLHttpRequest", StringComparison.OrdinalIgnoreCase );
}
