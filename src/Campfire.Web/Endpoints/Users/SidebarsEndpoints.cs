using Campfire.Application.Memberships;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Users;

/// <summary>
/// <c>Users::SidebarsController</c> (<c>app/controllers/users/sidebars_controller.rb</c>): the room list, loaded into
/// the sidebar's turbo frame.
/// </summary>
public static partial class SidebarsEndpoints
{
    /// <summary>
    /// The visible memberships, split into direct rooms (most recently updated first) and the rest, and up to 20
    /// users to start a direct room with. Each direct room's fragment is looked up before its view is built.
    /// </summary>
    private static async Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.Html );
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.MembershipsForNil );
        var viewContext = await Pages.ContextAsync( requestContext, cacheFragments: true );

        var page = await app.Database.ReadAsync( db =>
        {
            var sidebar = MembershipQueries.Sidebar( db, user.Id );
            var directMembers = DirectRoomMembers.ForUser( db, user.Id );

            return new SidebarPage(
                viewContext,
                UserPresentation.Summary( app, user ),
                app.Broadcasts.SignedRooms( ),
                app.Broadcasts.SignedUserRooms( user.Id ),
                [.. sidebar.DirectMemberships.Select( row => DirectItem( app, viewContext, row, directMembers[row.Room.Id], user ) )],
                [.. sidebar.DirectPlaceholderUsers.Select( placeholder => UserPresentation.Summary( app, placeholder ) )],
                [.. sidebar.OtherMemberships.Select( row => RoomPresentation.Shared( row.Room, row.Membership.IsUnread ) )],
                user.IsAdministrator || !viewContext.Account.RestrictRoomCreationToAdministrators );
        }, requestContext.RequestAborted );

        return await Pages.RenderAsync( requestContext, viewContext, Views.users.sidebars.show.Create( page ) );
    }

    private static SidebarDirectItem DirectItem(
        CampfireApp app,
        ViewContext viewContext,
        MembershipWithRoom row,
        IEnumerable<DirectRoomMember> members,
        User user )
    {
        return SidebarDirectItems.For( app, viewContext, row, members, SidebarDirectItems.Member( row.Room.Id, user ) );
    }
}
