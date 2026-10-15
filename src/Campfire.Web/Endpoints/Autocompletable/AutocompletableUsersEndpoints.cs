using Campfire.Application.Data;
using Campfire.Application.Rooms;
using Campfire.Application.Security;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Json;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Autocompletable;

/// <summary>
/// <c>Autocompletable::UsersController</c> (<c>app/controllers/autocompletable/users_controller.rb</c>): suggestions for
/// the rich text editor's mentions prompt (HTML <c>&lt;lexxy-prompt-item&gt;</c>s) and the user pickers (JSON).
/// </summary>
public static partial class AutocompletableUsersEndpoints
{
    /// <summary><c>set_page_and_extract_portion_from ..., per_page: 20</c>.</summary>
    public const int PerPage = 20;

    /// <summary>
    /// <c>params[:filter].presence || params[:query].presence</c>: the mentions prompt filters with <c>filter</c>, the
    /// autocomplete inputs with <c>query</c>.
    /// </summary>
    private static readonly string[] QueryKeys = [ParamNames.Autocompletion.Filter, ParamNames.Autocompletion.Query];

    /// <summary>
    /// A page of <c>find_autocompletable_users.with_attached_avatar.ordered</c>: the active users of the room
    /// <c>room_id</c> names (one of the current user's, else 404) or of the account, filtered by <c>filter</c> (the
    /// mentions prompt) or <c>query</c> (the pickers). HTML renders without a layout; JSON gets geared_pagination's
    /// <c>X-Total-Count</c> and <c>Link</c> headers (<c>set_paginated_headers</c>).
    /// </summary>
    private static async Task<FrontendResult> IndexAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var currentUser = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.RoomsForNil );
        var portion = GearedPortion.From( requestContext.Params[ParamNames.Page], PerPage );
        var roomId = RoomIdParam( requestContext );
        var query = QueryParam( requestContext );
        var (count, users) = await app.Database.ReadAsync( db =>
        {
            var scope = roomId is { } id ? FindRoom( db, currentUser.Id, id ).Id : ( long? ) null;
            var count = UserPages.CountAutocompletable( db, scope, query );
            return (count, UserPages.Autocompletable( db, scope, query, portion.Offset, portion.Limit ));
        }, requestContext.RequestAborted );

        portion.Store( requestContext, count );
        var format = requestContext.RespondTo( Mime.Html, Mime.Json );
        if ( format == Mime.Json )
        {
            var json = users.Select( user => AutocompletableUserJson.For(
                user.Name,
                user.Id,
                requestContext.UrlFor( UserPresentation.AvatarPath( app, user ) ),
                AttachableSignedGlobalId( app, user ) ) );
            return requestContext.Render( StatusCodes.Status200OK, Mime.Json, JbuilderViews.AutocompletableUsersIndex( json ) );
        }

        // format.html { render layout: false }
        var mentions = users.Select( user => new MentionUserView( UserPresentation.Summary( app, user ), AttachableSignedGlobalId( app, user ) ) ).ToList( );
        return await Pages.BareAsync(
            requestContext,
            Mime.Html,
            viewContext => Views.autocompletable.users.index.Create( new AutocompletableUsersPage( viewContext, mentions ) ) );
    }

    /// <summary>
    /// <c>params[:room_id].present?</c>: the room to scope to. An id the integer column can't cast finds no room, so it
    /// is 0, which no room has.
    /// </summary>
    private static long? RoomIdParam( RequestContext requestContext )
    {
        if ( requestContext.Params[ParamNames.Rooms.Id] is not { IsPresent: true } value )
        {
            return null;
        }

        return RailsIntegers.Cast( value.AsString ) ?? 0;
    }

    /// <summary><c>params[:filter].presence || params[:query].presence</c>.</summary>
    private static string? QueryParam( RequestContext requestContext )
    {
        foreach ( var key in QueryKeys )
        {
            if ( requestContext.Params[key] is not { IsPresent: true } value )
            {
                continue;
            }

            return value.AsString;
        }

        return null;
    }

    /// <summary><c>Current.user.rooms.find(params[:room_id])</c> (404 unless it's one of theirs).</summary>
    private static Room FindRoom( CampfireDb db, long userId, long roomId )
    {
        var room = RoomQueries.FindByIdForUser( db, userId, roomId );
        if ( room is null )
        {
            throw new RecordNotFoundException( ModelNames.Room );
        }

        return room;
    }

    /// <summary><c>user.attachable_sgid</c>.</summary>
    private static string AttachableSignedGlobalId( CampfireApp app, User user ) =>
        app.SignedGlobalIds.AttachableSignedGlobalId( GlobalId.For( ModelNames.User, user.Id ) );
}
