using System.Text.Json.Nodes;
using Campfire.Application.Data;
using Campfire.Application.Rooms;
using Campfire.Application.Security;
using Campfire.Web.App;
using Campfire.Web.Cable;

namespace Campfire.Web.Channels;

/// <summary>
/// <c>RoomMessagesChannel</c> (<c>app/channels/room_messages_channel.rb</c>): authorizes a room's message stream when
/// the subscription is made, so revoking a membership stops delivery. The room comes from the verified stream name
/// (<c>&lt;room gid param&gt;:messages</c>), never from a parameter, and the user must be a member;
/// <c>Turbo::StreamsChannel</c> turns these names away (<see cref="RoomStreamsAreAuthorized"/>), so this is the only
/// way onto them.
/// </summary>
/// <remarks>
/// Its public methods are actions: <c>subscribed</c>, which authorizes and streams again (or rejects), and
/// <c>verified_stream_name_from_params</c> from <c>include Turbo::Streams::StreamName::ClassMethods</c>, which only
/// verifies the name and sends nothing.
/// </remarks>
public sealed class RoomMessagesChannel( CampfireApp app ) : CableChannel<CableUser>
{
    /// <summary>The Ruby class name clients subscribe with (<c>RoomMessagesChannel</c>).</summary>
    public const string ClassName = "RoomMessagesChannel";

    public override async ValueTask SubscribedAsync( CancellationToken cancellationToken )
    {
        // authorized_stream_name: stream_name.present? (not blank) and a room the user is in.
        var streamName = TurboStreamsChannel.VerifiedStreamNameFromParams( Params, app.StreamNames.Verify );
        if ( !string.IsNullOrWhiteSpace( streamName ) && await SubscribableRoomAsync( CurrentUser.Id, streamName, cancellationToken ) )
        {
            StreamFrom( streamName );
        }
        else
        {
            Reject( );
        }
    }

    public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken )
    {
        if ( TurboStreamsChannel.PerformVerifiedStreamNameFromParams( action, Params, app.StreamNames.Verify ) )
        {
            return ValueTask.FromResult( true );
        }

        return PerformSubscribedAsync( action, cancellationToken );
    }

    /// <summary>
    /// <c>subscribable_room(user, stream_name)</c>: the suffix is <c>messages</c>, the GID param locates a room
    /// (<c>GlobalID::Locator.locate gid_param, only: Room</c>, which finds through the GID's own class, so an STI class
    /// only finds rooms of that type), and the user is in it.
    /// </summary>
    private async Task<bool> SubscribableRoomAsync( long userId, string streamName, CancellationToken cancellationToken )
    {
        if ( !RoomStreamsAreAuthorized.TrySplit( streamName, out var gidParam ) )
        {
            return false;
        }

        var globalId = GlobalId.FromParam( gidParam ) ?? GlobalId.Parse( gidParam );
        if ( globalId is null || !IsRoomModel( globalId.ModelName ) || RailsIntegers.Cast( globalId.Id ) is not { } roomId )
        {
            return false;
        }

        var room = await app.Database.ReadAsync( db => RoomQueries.FindByIdForUser( db, userId, roomId ), cancellationToken );
        return room is not null && IsLocatedAs( globalId.ModelName, room );
    }

    /// <summary>Whether a GID naming <paramref name="modelName"/> locates <paramref name="room"/>: its STI class, or <c>Room</c>.</summary>
    private static bool IsLocatedAs( string modelName, Room room ) =>
        modelName == ModelNames.Room || modelName == RoomTypes.ClassName( room.Type );

    private static bool IsRoomModel( string modelName ) =>
        modelName is ModelNames.Room or RoomTypes.OpenClassName or RoomTypes.ClosedClassName or RoomTypes.DirectClassName;
}
