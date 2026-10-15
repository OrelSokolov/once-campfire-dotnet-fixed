using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Browser message mutations require forms; JSON null is rejected without a partial write.</summary>
public sealed class NullMessageBodyTests
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long Second = SliceAppFactory.Id( "second" );

    [Fact]
    public async Task Creating_with_a_json_null_body_is_rejected_without_writes( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        var before = DatabaseSnapshot.Of( factory );

        var reply = await client.SendJsonAsync(
            HttpMethod.Post,
            Routes.RoomMessages( Designers ),
            token,
            """{"message":{"body":null,"client_message_id":"null-create"}}""",
            accept: "text/vnd.turbo-stream.html" );

        Assert.Equal( HttpStatusCode.UnsupportedMediaType, reply.Status );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        client.Dispose( );
    }

    [Fact]
    public async Task Updating_with_a_json_null_body_is_rejected_without_writes_or_broadcasts( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        factory.Execute( $"INSERT INTO message_search_index(rowid, body) VALUES ({Second}, 'Seconded.')" );
        using var taps = await SliceTaps.OnRoomAsync( factory, Designers );
        var before = DatabaseSnapshot.Of( factory );

        var reply = await client.SendJsonAsync(
            HttpMethod.Put,
            Routes.RoomMessage( Designers, Second ),
            token,
            """{"message":{"body":null}}""",
            accept: "application/json" );

        Assert.Equal( HttpStatusCode.UnsupportedMediaType, reply.Status );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        Assert.Empty( factory.Events.Only( "broadcast" ) );
        client.Dispose( );
    }
}
