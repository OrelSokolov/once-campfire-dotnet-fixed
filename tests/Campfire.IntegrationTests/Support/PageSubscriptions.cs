using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Cable;
using Campfire.Web.Channels;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// Every subscription the room page and the sidebar make for one user in one room,
/// plus the guarded stream the stock Turbo channel must turn away.
/// </summary>
internal sealed class PageSubscriptions
{
    private PageSubscriptions( List<PageSubscription> all, string guardedTurbo )
    {
        All = all;
        GuardedTurbo = guardedTurbo;
    }

    public IReadOnlyList<PageSubscription> All { get; }

    /// <summary><c>Turbo::StreamsChannel</c> with the room's message stream: always rejected (<c>RoomStreamsAreAuthorized</c>).</summary>
    public string GuardedTurbo { get; }

    public IEnumerable<string> Broadcastings =>
        All.Where( subscription => subscription.Broadcasting is not null ).Select( subscription => subscription.Broadcasting! );

    public static async Task<PageSubscriptions> ForAsync( CampfireApp app, long roomId, long userId )
    {
        ArgumentNullException.ThrowIfNull( app );

        var room = await app.Database.ReadAsync( db => RoomQueries.FindById( db, roomId )!, CancellationToken.None );
        var roomGlobalId = RoomBroadcasts.RoomGlobalIdParam( room );
        var signedMessages = app.Broadcasts.SignedRoomMessages( room );
        string Turbo( string channel, string signed ) => CableTestClient.Identifier( ("channel", channel), ("signed_stream_name", signed) );
        PageSubscription UserScoped( string channel, string? broadcasting ) =>
            new( channel, CableTestClient.Identifier( ("channel", channel) ), broadcasting, RoomScoped: false );
        PageSubscription RoomScoped( string channel )
        {
            var identifier = CableTestClient.Identifier( ("channel", channel), ("room_id", roomId) );
            return new( channel, identifier, CableNaming.BroadcastingFor( channel, roomGlobalId ), RoomScoped: true );
        }

        List<PageSubscription> all =
        [
            UserScoped( "HeartbeatChannel", broadcasting: null ),
            UserScoped( "ReadRoomsChannel", ReadRoomsChannel.StreamNameFor( userId ) ),
            UserScoped( "UnreadRoomsChannel", UnreadRoomsChannel.StreamNameFor( userId ) ),
            RoomScoped( "RoomChannel" ),
            RoomScoped( "PresenceChannel" ),
            RoomScoped( "TypingNotificationsChannel" ),
            new(
                "RoomMessagesChannel",
                Turbo( "RoomMessagesChannel", signedMessages ),
                CableNaming.StreamNameFrom( RoomBroadcasts.RoomMessages( room ) ),
                RoomScoped: true ),
            new(
                "Turbo::StreamsChannel :rooms",
                Turbo( TurboStreamsChannel.ClassName, app.Broadcasts.SignedRooms( ) ),
                RoomBroadcasts.RoomsStream,
                RoomScoped: false ),
            new(
                "Turbo::StreamsChannel [user, :rooms]",
                Turbo( TurboStreamsChannel.ClassName, app.Broadcasts.SignedUserRooms( userId ) ),
                CableNaming.StreamNameFrom( CableNaming.UserGlobalIdParam( userId ), RoomBroadcasts.RoomsStream ),
                RoomScoped: false ),
        ];

        return new PageSubscriptions( all, Turbo( TurboStreamsChannel.ClassName, signedMessages ) );
    }
}
