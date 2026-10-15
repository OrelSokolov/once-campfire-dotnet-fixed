using System.Globalization;
using System.Text;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using static Campfire.Web.Views.Caching.PageCacheNames;

namespace Campfire.Web.Views.Caching;

/// <summary>Called by authorized endpoint owners before their presentation reads, never before admission.</summary>
internal sealed class PageCacheRequest(
    PageResponseCache cache,
    RequestContext requestContext,
    string route,
    long generation,
    string key,
    CurrentSession session )
{
    /// <summary>The <c>Cache-Control</c> directive that keeps a response out of the cache.</summary>
    private const string NoStoreDirective = "no-store";

    /// <summary>The most a key field's length prefix takes: the length's digits and the colon.</summary>
    private const int KeyFieldPrefixCapacity = 12;

    /// <summary>
    /// The headers a stored page keeps, which are the same for each of the viewer's requests. <c>Link</c> is not one:
    /// a hit adds the preload links itself (<see cref="HitResult"/>), so a hit never carries them twice.
    /// </summary>
    private static readonly string[] StoredHeaders =
    [
        HeaderNames.ContentType,
        HeaderNames.ContentEncoding,
        HeaderNames.ETag,
        HeaderNames.Vary,
        HeaderNames.CacheControl,
    ];

    private IDisposable? _render;

    internal bool Hit { get; private set; }

    internal DateTimeOffset ExpiresAt { get; set; } = requestContext.Now + PageResponseCache.MaximumLifetime;

    internal void LimitLifetime( ReadOnlySpan<byte> plain ) =>
        ExpiresAt = PageClockFreshness.Limit( plain, requestContext.App( ).SignedGlobalIds, requestContext.Now, ExpiresAt );

    internal static async ValueTask<FrontendResult?> LookupAsync( RequestContext requestContext, string route )
    {
        var cache = requestContext.HttpContext.RequestServices.GetService<PageResponseCache>( );
        if ( cache is null )
        {
            return null;
        }

        if ( !cache.Enabled )
        {
            cache.Count( route, Events.Render, Reasons.Disabled );
            return null;
        }

        var rejected = Admission( requestContext );
        if ( rejected is not null )
        {
            cache.CountBypass( route, Reasons.NotAdmittedPrefix + rejected );
            return null;
        }

        var captured = RenderGeneration.Of( requestContext.HttpContext );
        if ( captured is null )
        {
            cache.CountBypass( route, Reasons.ObserverFailure );
            return null;
        }

        var key = Key( requestContext, route, captured.Value );
        if ( key is null )
        {
            cache.CountBypass( route, Reasons.KeyTooLong );
            return null;
        }

        var session = requestContext.CurrentSession( )!;
        var request = new PageCacheRequest( cache, requestContext, route, captured.Value, key, session );
        requestContext.HttpContext.Features.Set( request );
        var lookup = await cache.LookupAsync( route, captured.Value, key, requestContext.Request.IsHead, requestContext.RequestAborted );
        var entry = lookup.Entry;
        var render = lookup.Render;
        request._render = render;
        if ( render is not null )
        {
            requestContext.HttpContext.Response.RegisterForDispose( render );
        }

        if ( captured.Value != cache.NewestGeneration && !lookup.Coalesced )
        {
            requestContext.HttpContext.Features.Set<PageCacheRequest>( null );
            return null;
        }

        if ( entry is null )
        {
            return null;
        }

        request.Hit = true;
        var assets = requestContext.HttpContext.RequestServices.GetRequiredService<LayoutAssets>( );
        return HitResult( entry, route, requestContext.TurboFrameRequestId, assets );
    }

    /// <summary>
    /// A stored page, served again. A hit renders no layout, so it adds what <c>stylesheet_link_tag</c> would have:
    /// the preload links, on every cached page but the layout-less messages page, outside turbo-rails' frame layout
    /// (a non-blank <paramref name="turboFrameRequestId"/>).
    /// </summary>
    internal static FrontendResult HitResult( PageCacheEntry entry, string route, string? turboFrameRequestId, LayoutAssets assets )
    {
        var result = entry.Result( );
        if ( route != PageRoutes.Messages && string.IsNullOrWhiteSpace( turboFrameRequestId ) )
        {
            assets.AddPreloadLinks( result.Headers );
        }

        return result;
    }

    internal void Store( FrontendResult result )
    {
        if ( Hit )
        {
            return;
        }

        if ( StoreRejection( result ) is { } rejected )
        {
            cache.Count( route, Events.Miss, Reasons.NotAdmittedPrefix + rejected );
            return;
        }

        var body = SegmentedPageBody.Copy( result.Body );
        if ( body is null )
        {
            cache.Count( route, Events.Miss, Reasons.Oversize );
            return;
        }

        var headers = StoredHeaders
            .Where( result.Headers.ContainsKey )
            .Select( name => new KeyValuePair<string, string>( name, string.Join( ", ", result.Headers[name].ToArray( ) ) ) )
            .ToArray( );
        var entry = new PageCacheEntry( body, headers, ExpiresAt );
        cache.Store( route, generation, key, entry );
        PageResponseCache.Publish( _render, entry );
    }

    /// <summary>Why the response may not be stored, if it may not: the request's admission still holds, and the response is a plain page.</summary>
    private string? StoreRejection( FrontendResult result )
    {
        if ( Admission( requestContext ) is { } rejected )
        {
            return rejected;
        }

        if ( !requestContext.Request.IsGet )
        {
            return Rejections.Method;
        }

        if ( requestContext.CurrentSession( ) != session )
        {
            return Rejections.SessionChanged;
        }

        if ( result.Status != StatusCodes.Status200OK )
        {
            return Rejections.Status;
        }

        if ( result.Header( HeaderNames.CacheControl )?.Contains( NoStoreDirective, StringComparison.OrdinalIgnoreCase ) == true )
        {
            return Rejections.NoStore;
        }

        return null;
    }

    private static string? Admission( RequestContext requestContext )
    {
        if ( requestContext.Request is { IsGet: false, IsHead: false } )
        {
            return Rejections.Method;
        }

        if ( requestContext.AuthenticatedBy( ) != AuthenticatedBy.Session || requestContext.CurrentSession( ) is null )
        {
            return Rejections.Session;
        }

        if ( requestContext.CurrentSession( )!.User.Record is User { IsBot: true } )
        {
            return Rejections.Bot;
        }

        if ( !requestContext.Flash.IsEmpty )
        {
            return Rejections.Flash;
        }

        // These endpoint owners hold no writer lease or transaction while rendering. A live response that
        // mutates credentials is still rejected by the session identity comparison before storing.
        return null;
    }

    internal static string? Key( RequestContext requestContext, string route, long generation )
    {
        return Key( requestContext.HttpContext, requestContext.CurrentSession( ), route, generation, requestContext.Param( ParamNames.Format ) );
    }

    /// <summary>
    /// The page's key: everything its output may depend on, each field prefixed with its length (<c>-1:</c> for a
    /// missing one). <see langword="null"/> when the key would be longer than <see cref="PageResponseCache.MaximumKeyChars"/>.
    /// </summary>
    internal static string? Key( HttpContext httpContext, CurrentSession? session, string route, long generation, string? format, string? encoding = null )
    {
        var request = httpContext.Request;
        string?[] fields =
        [
            route,
            generation.ToString( CultureInfo.InvariantCulture ),
            session?.User.Id.ToString( CultureInfo.InvariantCulture ),
            session?.Id.ToString( CultureInfo.InvariantCulture ),
            session?.Token,
            httpContext.Features.Get<IHttpRequestFeature>( )?.RawTarget ?? request.PathBase.Add( request.Path ).ToUriComponent( ) + request.QueryString,
            request.Scheme + "://" + request.Host.Value,
            encoding ?? Compression.NegotiatedEncoding( request ),
            format,
            request.Headers.Accept.ToString( ),
            request.ContentType,
            request.Headers[HeaderNames.XRequestedWith].ToString( ),
            FirstHeader( httpContext, CampfireHeaderNames.TurboFrame ),
            ParsedLastRoom( request.Cookies[ViewContexts.LastRoomCookie] ),
            FirstHeader( httpContext, HeaderNames.UserAgent ),
            CultureInfo.CurrentCulture.Name,
            CultureInfo.CurrentUICulture.Name,
        ];
        var capacity = 0;
        foreach ( var field in fields )
        {
            capacity += (field?.Length ?? 0) + KeyFieldPrefixCapacity;
        }

        if ( capacity > PageResponseCache.MaximumKeyChars )
        {
            return null;
        }

        var key = new StringBuilder( capacity );
        foreach ( var field in fields )
        {
            if ( field is null )
            {
                key.Append( "-1:" );
            }
            else
            {
                key.Append( field.Length.ToString( CultureInfo.InvariantCulture ) ).Append( ':' ).Append( field );
            }
        }

        return key.ToString( );
    }

    /// <summary>The first value of the request header <paramref name="name"/>, if it has one.</summary>
    internal static string? FirstHeader( HttpContext httpContext, string name ) =>
        httpContext.Request.Headers.TryGetValue( name, out var values ) && values.Count > 0 ? values[0] : null;

    private static string ParsedLastRoom( string? value ) => RailsIntegers.Cast( value )?.ToString( CultureInfo.InvariantCulture ) ?? "";
}
