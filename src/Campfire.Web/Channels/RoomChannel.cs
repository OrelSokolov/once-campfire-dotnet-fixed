using System.Text.Json.Nodes;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Cable;
using Campfire.Web.Paths;

namespace Campfire.Web.Channels;

/// <summary>
/// <c>RoomChannel</c> (<c>app/channels/room_channel.rb</c>): streams for the room the user is in
/// (<c>current_user.rooms.find_by(id: params[:room_id])</c>), or rejects. <c>PresenceChannel</c> and
/// <c>TypingNotificationsChannel</c> subclass it.
/// </summary>
/// <remarks>
/// Its <c>subscribed</c> is public, so Action Cable also takes it as an action (<c>Channel::Base.action_methods</c> is
/// every public method below <c>Base</c>): a client that performs <c>subscribed</c> runs it again, which finds the room
/// afresh and streams it a second time (every broadcast then arrives twice), or rejects if the membership is gone. The
/// <c>on_subscribe</c> callbacks of the subclasses don't run for it.
/// </remarks>
public class RoomChannel( CampfireApp app ) : CableChannel<CableUser>
{
    /// <summary>The Ruby class name clients subscribe with (<c>RoomChannel</c>).</summary>
    public const string ClassName = "RoomChannel";

    protected CampfireApp App { get; } = app;

    /// <summary><c>@room</c>, once subscribed.</summary>
    protected Room? Room { get; private set; }

    public override ValueTask SubscribedAsync( CancellationToken cancellationToken ) => StreamRoomAsync( cancellationToken );

    public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken ) =>
        PerformSubscribedAsync( action, cancellationToken );

    /// <summary>
    /// <c>params[:room_id]</c> as Active Record casts it for an integer <c>id</c> condition: integers as they are,
    /// floats truncated, <c>true</c>/<c>false</c> as 1/0, strings through <c>to_i</c> unless they don't start like a
    /// number; anything else, or out of range, finds nothing.
    /// </summary>
    internal static long? CastRoomId( object? value ) => value switch
    {
        string text => RailsIntegers.Cast( text ),
        long integer => integer,
        double real => double.IsFinite( real ) && Math.Abs( real ) < long.MaxValue ? ( long ) Math.Truncate( real ) : null,
        true => 1,
        false => 0,
        _ => null,
    };

    /// <summary><c>subscribed</c> without the subclasses' <c>on_subscribe</c> callbacks.</summary>
    protected override ValueTask ResubscribeAsync( CancellationToken cancellationToken ) => StreamRoomAsync( cancellationToken );

    /// <summary><c>RoomChannel#subscribed</c>: <c>@room = find_room</c>, then <c>stream_for @room</c> or <c>reject</c>.</summary>
    private async ValueTask StreamRoomAsync( CancellationToken cancellationToken )
    {
        var userId = CurrentUser.Id;
        var roomId = CastRoomId( Params.GetValueOrDefault( ParamNames.Rooms.Id ) );
        if ( roomId is { } id )
        {
            Room = await App.Database.ReadAsync( db => RoomQueries.FindByIdForUser( db, userId, id ), cancellationToken );
        }
        else
        {
            Room = null;
        }

        if ( Room is { } room )
        {
            StreamFor( RoomBroadcasts.RoomGlobalIdParam( room ) );
        }
        else
        {
            Reject( );
        }
    }
}
