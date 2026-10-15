using System.Net;
using System.Text.RegularExpressions;

using Campfire.Application.Integrations.Network;

using Campfire.Application.Ruby;
using Microsoft.Extensions.Logging;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary>
/// <c>Opengraph::Location</c> (<c>app/models/opengraph/location.rb</c>): a URL that is valid when it parses as http(s)
/// and its host resolves to a public address, which is memoized and pinned for the fetch.
/// </summary>
internal sealed partial class OpengraphLocation
{
    private readonly OpengraphFetchContext _fetchContext;

    private readonly string? _url;

    private readonly RubyUri? _parsedUrl;

    private bool _resolved;

    private IPAddress? _resolvedIp;

    /// <summary><c>Location.new(url)</c>: <c>parsed_url</c> is <c>URI.parse(url) rescue nil</c>.</summary>
    public OpengraphLocation( OpengraphFetchContext fetchContext, string? url )
    {
        _fetchContext = fetchContext;
        _url = url;
        _parsedUrl = url is not null && RubyUri.TryParse( url, out var parsed ) == RubyUriError.None ? parsed : null;
    }

    /// <summary><c>valid?</c>: both validations run, so the host is resolved even for a URL that isn't http(s).</summary>
    public async Task<bool> IsValidAsync( CancellationToken cancellationToken )
    {
        var isHttp = _parsedUrl?.IsHttp ?? false;
        var isPublic = await ResolvedIpAsync( cancellationToken ) is not null;
        return isHttp && isPublic;
    }

    /// <summary><c>resolved_ip</c>: <c>PrivateNetworkGuard.resolve(parsed_url.host) rescue nil</c>, memoized.</summary>
    public async Task<IPAddress?> ResolvedIpAsync( CancellationToken cancellationToken )
    {
        if ( !_resolved )
        {
            _resolvedIp = _parsedUrl is null ? null : await PrivateNetworkGuard.TryResolveAsync( _fetchContext.Resolver, _parsedUrl.Host, cancellationToken );
            _resolved = true;
        }

        return _resolvedIp;
    }

    /// <summary><c>read_html</c>: nothing for an invalid URL or one that looks like a file or media, else the fetched page.</summary>
    public async Task<byte[]?> ReadHtmlAsync( CancellationToken cancellationToken )
    {
        if ( !await IsValidAsync( cancellationToken ) || FilesAndMediaUrl( ).IsMatch( _url! ) )
        {
            return null;
        }

        try
        {
            return await OpengraphFetch.FetchDocumentAsync( _fetchContext, _parsedUrl!, _resolvedIp!, cancellationToken );
        }
        catch ( Exception error ) when ( IsFetchFailure( error ) )
        {
            LogFailure( error );
            return null;
        }
    }

    /// <summary><c>fetch_content_type</c>: the image's <c>Content-Type</c>, or null when it's invalid or the fetch fails.</summary>
    public async Task<string?> FetchContentTypeAsync( CancellationToken cancellationToken )
    {
        if ( !await IsValidAsync( cancellationToken ) )
        {
            return null;
        }

        try
        {
            return await OpengraphFetch.FetchContentTypeAsync( _fetchContext, _parsedUrl!, _resolvedIp!, cancellationToken );
        }
        catch ( Exception error ) when ( IsFetchFailure( error ) )
        {
            LogFailure( error );
            return null;
        }
    }

    /// <summary>What <c>rescue =&gt; e</c> catches around a fetch: anything but the unfurl being cancelled.</summary>
    private static bool IsFetchFailure( Exception error ) =>
        error is NetHttpException or OpengraphFetchException or PrivateNetworkViolationException or HostUnresolvableException;

    private void LogFailure( Exception error )
    {
        if ( _fetchContext.Logger.IsEnabled( LogLevel.Warning ) )
        {
            _fetchContext.Logger.LogWarning( "Failed to fetch {Url} at {Address} ({Error})", _parsedUrl, _resolvedIp, error.Message );
        }
    }

    /// <summary>The file and media extensions <c>FILES_AND_MEDIA_URL_REGEX</c> lists.</summary>
    private const string FileAndMediaExtensions =
        "zip|tar|tar\\.gz|tar\\.bz2|tar\\.xz|gz|bz2|rar|7z|dmg|exe|msi|pkg|deb|iso|jpg|jpeg|png|gif|bmp|mp4|mov|avi|mkv|wmv|flv|heic|heif"
        + "|mp3|wav|ogg|aac|wma|webm|ogv|mpg|mpeg";

    /// <summary><c>FILES_AND_MEDIA_URL_REGEX</c>. Non-backtracking, as the URL comes from any signed-in user.</summary>
    [GeneratedRegex( @"\bhttps?://\S+\.(?:" + FileAndMediaExtensions + @")\b", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking )]
    private static partial Regex FilesAndMediaUrl( );
}
