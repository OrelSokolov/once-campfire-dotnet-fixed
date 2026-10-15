using System.Globalization;
using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Rooms;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Json;
using Campfire.Web.Views.Models;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Endpoints.Messages;

/// <summary><c>Messages::ByBotsController#index</c>: a page of the room's messages, with the pagination headers.</summary>
public static partial class BotMessagesEndpoints
{
    /// <summary><c>headers["X-Total-Count"]</c>.</summary>
    public const string TotalCountHeader = CampfireHeaderNames.TotalCount;

    /// <summary>
    /// <c>index</c>: <c>find_paged_messages</c> (the page before or after a message, which must be the room's, else the
    /// last page), <c>set_pagination_headers</c>, then the implicit render: <c>messages/by_bots/index.json.jbuilder</c>
    /// for the route's JSON. With a <c>.html</c> extension the lookup falls through to the parent's
    /// <c>messages/index.html.erb</c> (<c>layout false</c>); any other format has no template (406).
    /// </summary>
    private static async Task<FrontendResult> IndexAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = RoomsEndpoints.RoomOf( requestContext );
        var before = Present( requestContext, ParamNames.Messages.Before );
        var after = Present( requestContext, ParamNames.Messages.After );

        var (messages, total, next) = await app.Database.ReadAsync( db =>
        {
            var page = FindPagedMessages( db, room.Id, before, after );
            return (page, MessageQueries.CountInRoom( db, room.Id ), NextPage( db, room.Id, page, after is not null ));
        }, requestContext.RequestAborted );

        requestContext.ResponseHeaders[TotalCountHeader] = total.ToString( CultureInfo.InvariantCulture );
        if ( next is { } page )
        {
            var botKey = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.BotKeyForNil ).BotKey;
            var nextPage = UrlEncoding.WithQuery( Routes.RoomBotMessages( room.Id, botKey ), new KeyValuePair<string, QueryValue>( page.Key, page.Id ) );
            requestContext.ResponseHeaders[HeaderNames.Link] = $"<{requestContext.UrlFor( nextPage )}>; rel=\"next\"";
        }

        if ( requestContext.RespondTo( Mime.Json, Mime.Html ) == Mime.Html )
        {
            return await RenderHtmlAsync( requestContext, messages );
        }

        var presenters = requestContext.MessagePresentersWithoutPreviews( );
        var json = await app.Database.ReadAsync(
            db => JbuilderViews.MessagesByBotsIndex( presenters.Json( db ).MessageEntries( messages ), presenters.FragmentCache ),
            requestContext.RequestAborted );
        return requestContext.Render( StatusCodes.Status200OK, Mime.Json, json );
    }

    /// <summary>
    /// <c>find_paged_messages</c>: <c>page_before(@room.messages.find(params[:before]))</c>, else
    /// <c>page_after(...find(params[:after]))</c>, else <c>last_page</c>. A message outside the room is
    /// <c>RecordNotFound</c> (404).
    /// </summary>
    private static List<Message> FindPagedMessages( CampfireDb db, long roomId, string? before, string? after )
    {
        if ( before is not null )
        {
            return MessagePages.PageBefore( db, roomId, FindInRoom( db, roomId, before ) );
        }

        if ( after is not null )
        {
            return MessagePages.PageAfter( db, roomId, FindInRoom( db, roomId, after ) );
        }

        return MessagePages.LastPage( db, roomId );
    }

    /// <summary>
    /// <c>next_page_params</c>: with <c>params[:after]</c>, <c>{ after: last.id }</c> when newer messages exist; otherwise
    /// <c>{ before: first.id }</c> when older ones exist; nothing for an empty page.
    /// </summary>
    private static (string Key, long Id)? NextPage( CampfireDb db, long roomId, List<Message> page, bool after )
    {
        if ( page.Count == 0 )
        {
            return null;
        }

        if ( after )
        {
            return MessagePages.ExistsAfter( db, roomId, page[^1] ) ? (ParamNames.Messages.After, page[^1].Id) : null;
        }

        return MessagePages.ExistsBefore( db, roomId, page[0] ) ? (ParamNames.Messages.Before, page[0].Id) : null;
    }

    /// <summary><c>messages/index.html.erb</c> without a layout: the page's messages, each from its fragment when cached.</summary>
    private static async Task<FrontendResult> RenderHtmlAsync( RequestContext requestContext, List<Message> messages )
    {
        var app = requestContext.App( );
        var presenters = await requestContext.MessagePresentersAsync( );
        var viewContext = await requestContext.Views( ).ForRequestAsync( requestContext, cacheFragments: true );
        var items = await app.Database.ReadAsync( db => presenters.For( db ).Items( viewContext, messages ), requestContext.RequestAborted );
        return await Pages.BareRenderAsync( requestContext, Mime.Html, Views.messages.index.Create( new MessagesIndexPage( viewContext, items ) ) );
    }

    /// <summary><c>params[key].present?</c>, as the string <c>find</c> casts.</summary>
    private static string? Present( RequestContext requestContext, string key ) =>
        requestContext.Params[key] is { IsPresent: true } value ? value.AsString ?? value.ToString( ) : null;

    /// <summary><c>@room.messages.find(id)</c>.</summary>
    private static Message FindInRoom( CampfireDb db, long roomId, string id )
    {
        if ( RailsIntegers.Cast( id ) is not { } messageId )
        {
            throw new RecordNotFoundException( ModelNames.Message );
        }

        return MessageQueries.FindInRoom( db, roomId, messageId );
    }
}
