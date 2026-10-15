using Campfire.Application.Data;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Caching;

namespace Campfire.Web.Endpoints;

/// <summary>
/// The message presenters of one request, built from what they all share: the app, <c>Current.request_host</c>,
/// whether ffmpeg exists (for video previews) and the request's base URL (for the JSON entries' URLs). Get them with
/// <see cref="MessagePresenterRequests.MessagePresentersAsync"/>.
/// </summary>
public readonly struct MessagePresenters( CampfireApp app, RequestContext requestContext, bool ffmpegExists )
{
    /// <summary>The fragment cache of the request's JSON views (their <c>json.cache!</c> entries).</summary>
    public FragmentCache? FragmentCache => requestContext.Views( ).ForRequestCache( requestContext );

    /// <summary>The HTML presenter over a leased connection, for messages of <paramref name="room"/> when it's given.</summary>
    public MessagePresenter For( CampfireDb db, Room? room = null ) => new( app, db, requestContext.Request.Host, ffmpegExists, room );

    /// <summary>The JSON presenter (the bot API's jbuilder views) over a leased connection.</summary>
    public MessageJsonPresenter Json( CampfireDb db ) => new( app, db, requestContext.UrlFor( "" ), requestContext.Request.Host );
}
