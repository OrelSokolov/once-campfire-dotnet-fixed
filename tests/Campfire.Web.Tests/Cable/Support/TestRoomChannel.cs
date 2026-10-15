using System.Text.Json.Nodes;
using Campfire.Web.Cable;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary>Like Campfire's <c>RoomChannel</c> plus <c>TypingNotificationsChannel</c>'s actions, logging its callbacks.</summary>
public sealed class TestRoomChannel( ChannelLog log ) : CableChannel<TestUser>
{
    private string? _room;

    public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
    {
        var roomId = Params.GetValueOrDefault( "room_id" ) is long id && CurrentUser.RoomIds.Contains( id ) ? id : ( long? ) null;
        if ( roomId is null )
        {
            Reject( );
        }
        else
        {
            _room = $"room-{roomId}";
            if ( Params.GetValueOrDefault( "greet" ) is not null )
            {
                Transmit( new JsonObject { ["hello"] = CurrentUser.Name } );
            }

            StreamFor( _room );
        }

        log.Add( $"subscribed rejected={IsRejected.ToString( ).ToLowerInvariant( )}" );

        return ValueTask.CompletedTask;
    }

    public override ValueTask UnsubscribedAsync( CancellationToken cancellationToken )
    {
        log.Add( $"unsubscribed rejected={IsRejected.ToString( ).ToLowerInvariant( )}" );

        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken )
    {
        switch ( action )
        {
            case "start":
                BroadcastTo( [_room!], new JsonObject { ["action"] = "start", ["user"] = new JsonObject { ["id"] = CurrentUser.Id } } );
                break;
            case "echo":
                Transmit( data.DeepClone( ) );
                break;
            case "receive":
                Transmit( new JsonObject { ["received"] = data.DeepClone( ) } );
                break;
            case "explode":
                throw new InvalidOperationException( "boom" );
            default:
                return ValueTask.FromResult( false );
        }

        return ValueTask.FromResult( true );
    }
}
