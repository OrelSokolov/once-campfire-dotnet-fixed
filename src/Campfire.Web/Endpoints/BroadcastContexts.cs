using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints;

/// <summary>The view context of the broadcasts an endpoint makes.</summary>
public static class BroadcastContexts
{
    /// <summary>
    /// The view context <c>ApplicationController.render</c> gives a broadcast made during this request (no user, no
    /// tokens, the request's protocol and host). Called once the write committed, so it never takes the request's token.
    /// </summary>
    public static Task<ViewContext> BroadcastContextAsync( this RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var views = requestContext.Views( );
        var baseUrl = ViewContexts.RendererBaseUrl( requestContext );
        var cache = views.CaptureBroadcastCache( baseUrl );
        var database = requestContext.Database( );
        return database.ReadAsync( db => views.ForBroadcast( db, baseUrl, cache ), CancellationToken.None );
    }
}
