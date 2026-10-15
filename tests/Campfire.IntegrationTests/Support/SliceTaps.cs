using System.Globalization;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Web.Channels;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// Taps on the app's in-process pub/sub: every broadcast on a room's message stream and on its members' unread
/// streams is recorded in <see cref="SliceEvents"/> as <c>broadcast …</c> and <c>unread user …</c>.
/// </summary>
public sealed class SliceTaps : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    private SliceTaps( )
    {
    }

    public static async Task<SliceTaps> OnRoomAsync( SliceAppFactory factory, long roomId )
    {
        ArgumentNullException.ThrowIfNull( factory );

        var app = factory.App;
        var (room, members) = await app.Database.ReadAsync(
            db => (RoomQueries.FindById( db, roomId )!, MembershipQueries.UserIdsInRoom( db, roomId )),
            TestContext.Current.CancellationToken );

        var taps = new SliceTaps( );
        var roomMessages = string.Join( ':', RoomBroadcasts.RoomMessages( room ) );
        taps._subscriptions.Add( app.Cable.Hub.Subscribe( roomMessages, payload => factory.Events.Record( $"broadcast {Action( payload )}" ) ) );
        foreach ( var userId in members )
        {
            var id = userId.ToString( CultureInfo.InvariantCulture );
            taps._subscriptions.Add( app.Cable.Hub.Subscribe( UnreadRoomsChannel.StreamNameFor( userId ), _ => factory.Events.Record( $"unread user {id}" ) ) );
        }

        return taps;
    }

    public void Dispose( ) => _subscriptions.ForEach( subscription => subscription.Dispose( ) );

    /// <summary>The turbo-stream action of a broadcast payload.</summary>
    private static string Action( string payload )
    {
        var start = payload.IndexOf( "action=\\\"", StringComparison.Ordinal );
        if ( start < 0 )
        {
            return payload.Length > 40 ? payload[..40] : payload;
        }

        start += "action=\\\"".Length;
        var end = payload.IndexOf( '\\', start );
        return payload[start..end];
    }
}
