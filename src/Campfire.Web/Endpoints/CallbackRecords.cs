using Campfire.Application.Memberships;
using Campfire.Application.Users;
using Campfire.Web.Http;
using Campfire.Web.Policy;

namespace Campfire.Web.Endpoints;

/// <summary>
/// The records a controller's <c>before_action</c> callbacks (<c>set_room</c>, <c>set_user</c>, <c>set_message</c>, …)
/// keep on the request for the action. The action runs only after its callbacks, so a missing record is a bug in the
/// endpoint's callback chain, not a request error.
/// </summary>
public static class CallbackRecords
{
    /// <summary>What <paramref name="callbackName"/> stored on the request.</summary>
    public static T Required<T>( this RequestContext requestContext, string callbackName )
        where T : class
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        return requestContext.Current<T>( ) ?? throw new InvalidOperationException( $"{callbackName} did not run" );
    }

    /// <summary><c>@user</c> (or <c>@bot</c>), as the controller's <c>set_user</c> or <c>set_bot</c> found it.</summary>
    public static User SelectedUser( this RequestContext requestContext ) =>
        requestContext.Required<SelectedUser>( "set_user" ).User;

    /// <summary>
    /// <c>@membership</c> of a room-scoped controller (<c>RoomScoped#set_room</c>: the user's membership, with
    /// <c>@room</c> through it).
    /// </summary>
    public static MembershipWithRoom RoomMembership( this RequestContext requestContext ) =>
        requestContext.Required<CurrentMembership>( "set_room" ).Record as MembershipWithRoom
        ?? throw new InvalidOperationException( "set_room did not find a membership with its room" );
}
