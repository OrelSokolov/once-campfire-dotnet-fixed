using System.Globalization;
using Campfire.Application.Data;
using Campfire.Application.Rooms;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Presenters;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using static Campfire.Web.Views.Caching.PageCacheNames;

namespace Campfire.Web.Views.Caching;

/// <summary>Serve admitted page hits before allocating the routed browser pipeline; misses keep its ownership.</summary>
internal sealed class PageCacheHitMiddleware(
    RequestDelegate next,
    PageResponseCache cache,
    DatabaseGeneration observer,
    CampfireDatabase database,
    IOptionsMonitor<CookieAuthenticationOptions> cookies,
    LayoutAssets assets,
    PageAuthorityCache authority,
    PageTicketMemo tickets,
    IResponseCompressionProvider compression )
{
    /// <summary>The start of a room page's path, <c>/rooms/&lt;id&gt;</c>.</summary>
    private const string RoomPathPrefix = Routes.Rooms + "/";

    public async Task InvokeAsync( HttpContext httpContext )
    {
        if ( !cache.Enabled
            || !TryRoute( httpContext, out var route, out var roomId )
            || httpContext.Request.Cookies.ContainsKey( FlashCookie.Name )
            || !Unambiguous( httpContext ) )
        {
            await next( httpContext );
            return;
        }

        var credential = tickets.Read( httpContext, cookies.Get( BrowserAuthentication.Scheme ) );
        if ( credential is null )
        {
            await next( httpContext );
            return;
        }

        // The exact observation precedes every authority and resource read. Do not hand credentials or
        // captured state to authentication on a miss: its rejection, sign-out and activity rules stay owned there.
        var generation = observer.Capture( );
        if ( generation is null )
        {
            await next( httpContext );
            return;
        }

        var current = authority.Session( generation.Value, credential.Value.SessionId );
        var roomAllowed = current is not null && (roomId is null || authority.Room( generation.Value, current.KeySession.User.Id, roomId.Value ));
        if ( current is null || !roomAllowed )
        {
            await PopulateAuthorityAsync( httpContext, credential.Value, generation.Value, roomId );
            await next( httpContext );
            return;
        }

        if ( !current.Accepts( credential.Value.Token, database.Clock.GetUtcNow( ).UtcDateTime ) )
        {
            await next( httpContext );
            return;
        }

        var session = current.KeySession;

        var encoding = Compression.NegotiatedEncoding( httpContext.Request, compression );
        var format = httpContext.Request.Query[ParamNames.Format].FirstOrDefault( );
        var key = PageCacheRequest.Key( httpContext, session, route, generation.Value, format, encoding );
        var head = HttpMethodChecks.IsHead( httpContext.Request.Method );
        var entry = key is null ? null : cache.Lookup( route, generation.Value, key, head, out _, PageLookupMode.Probe );
        if ( entry is null || generation.Value != cache.NewestGeneration )
        {
            await next( httpContext );
            return;
        }

        var turboFrameRequestId = PageCacheRequest.FirstHeader( httpContext, CampfireHeaderNames.TurboFrame );
        var result = PageCacheRequest.HitResult( entry, route, turboFrameRequestId, assets );
        if ( route == PageRoutes.Room )
        {
            new RequestCookies( httpContext ).SetPreference( ViewContexts.LastRoomCookie, roomId!.Value.ToString( CultureInfo.InvariantCulture ) );
        }

        cache.Count( route, Events.EarlyHit );
        await EncodedCacheBody.WriteAsync( httpContext, result );
    }

    private Task<bool> PopulateAuthorityAsync( HttpContext httpContext, CableCredential credential, long generation, long? roomId ) => database.ReadAsync( db =>
    {
        var found = BrowserAuthentication.FindSessionById( db, credential.SessionId );
        if ( found is null )
        {
            return false;
        }

        var snapshot = PageSessionAuthority.From( found );
        if ( !snapshot.Accepts( credential.Token, database.Clock.GetUtcNow( ).UtcDateTime ) )
        {
            return false;
        }

        authority.StoreSession( generation, snapshot );
        if ( roomId is { } room && RoomQueries.FindByIdForUser( db, found.User.Id, room ) is not null )
        {
            authority.StoreRoom( generation, found.User.Id, room );
        }

        return true;
    }, httpContext.RequestAborted );

    private static bool Unambiguous( HttpContext httpContext )
    {
        if ( httpContext.Features.Get<IHttpRequestBodyDetectionFeature>( )?.CanHaveBody == true )
        {
            return false;
        }

        foreach ( var (name, values) in httpContext.Request.Query )
        {
            if ( values.Count != 1 || name.Contains( '[', StringComparison.Ordinal ) || name.Contains( ']', StringComparison.Ordinal ) )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The cached page a GET or HEAD path asks for: the sidebar, the search page, a room (<c>/rooms/&lt;id&gt;</c>) or
    /// its messages (<c>/rooms/&lt;id&gt;/messages</c>), with the room's id.
    /// </summary>
    internal static bool TryRoute( HttpContext httpContext, out string route, out long? roomId )
    {
        route = "";
        roomId = null;
        if ( !HttpMethodChecks.IsGetOrHead( httpContext.Request.Method ) )
        {
            return false;
        }

        var path = httpContext.Request.Path.Value.AsSpan( );
        if ( path.SequenceEqual( Routes.UserSidebar ) )
        {
            route = PageRoutes.Sidebar;
            return true;
        }

        if ( path.SequenceEqual( Routes.Searches ) )
        {
            route = PageRoutes.Search;
            return true;
        }

        if ( !path.StartsWith( RoomPathPrefix, StringComparison.Ordinal ) )
        {
            return false;
        }

        path = path[RoomPathPrefix.Length..];
        var slash = path.IndexOf( '/' );
        var id = slash < 0 ? path : path[..slash];
        if ( !IsRoomId( id, out var parsed ) )
        {
            return false;
        }

        if ( slash < 0 )
        {
            route = PageRoutes.Room;
        }
        else if ( path[slash..].SequenceEqual( Routes.Messages ) )
        {
            route = PageRoutes.Messages;
        }
        else
        {
            return false;
        }

        roomId = parsed;
        return true;
    }

    /// <summary>A path segment of ASCII digits only that fits a room id.</summary>
    private static bool IsRoomId( ReadOnlySpan<char> segment, out long roomId )
    {
        roomId = 0;
        return !segment.IsEmpty
            && segment.IndexOfAnyExceptInRange( '0', '9' ) < 0
            && long.TryParse( segment, NumberStyles.None, CultureInfo.InvariantCulture, out roomId );
    }
}
