using System.Text.Json.Nodes;
using Campfire.Web.App;

namespace Campfire.Web.Channels;

/// <summary>
/// <c>TypingNotificationsChannel &lt; RoomChannel</c>: <c>start</c> and <c>stop</c> broadcast
/// <c>{ action:, user: current_user.slice(:id, :name) }</c> to the room.
/// </summary>
public sealed class TypingNotificationsChannel( CampfireApp app ) : RoomChannel( app )
{
    /// <summary>The Ruby class name clients subscribe with (<c>TypingNotificationsChannel</c>).</summary>
    public new const string ClassName = "TypingNotificationsChannel";

    private const string StartAction = "start";

    private const string StopAction = "stop";

    public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken )
    {
        if ( action is not (StartAction or StopAction) )
        {
            return base.PerformAsync( action, data, cancellationToken );
        }

        if ( Room is not { } room )
        {
            throw new InvalidOperationException( $"TypingNotificationsChannel#{action} without a room" );
        }

        var user = CurrentUser;
        var notification = new JsonObject
        {
            ["action"] = action,
            ["user"] = new JsonObject { ["id"] = user.Id, ["name"] = user.Name },
        };
        BroadcastTo( [RoomBroadcasts.RoomGlobalIdParam( room )], notification );

        return ValueTask.FromResult( true );
    }
}
