using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Version-only preloads must preserve the dependencies of warm and mixed message-fragment pages.</summary>
public sealed class MessageFragmentInvalidationTests
{
    [Fact]
    public async Task Warm_pages_refresh_creator_and_booster_names_without_touching_the_message( )
    {
        using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var room = SliceAppFactory.Id( "watercooler" );
        var path = Routes.Room( room );
        var initial = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, initial.Status );
        var warm = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, warm.Status );

        var creator = app.Scalar<long>( $"SELECT creator_id FROM messages WHERE room_id = {room} ORDER BY created_at DESC LIMIT 1" );
        var booster = app.Scalar<long>( $"""
            SELECT b.booster_id FROM boosts b JOIN messages m ON m.id = b.message_id
            WHERE m.room_id = {room} AND b.booster_id <> {creator} LIMIT 1
            """ );
        Assert.NotEqual( 0, booster );
        app.Execute( $"""
            UPDATE users SET name = 'Fresh fragment creator', updated_at = '2026-09-27 14:00:00' WHERE id = {creator};
            UPDATE users SET name = 'Fresh fragment booster', updated_at = '2026-09-27 14:00:00' WHERE id = {booster};
            """ );

        var changed = await client.GetAsync( path );
        var cachedAgain = await client.GetAsync( path );
        foreach ( var reply in new[] { changed, cachedAgain } )
        {
            Assert.Equal( HttpStatusCode.OK, reply.Status );
            Assert.Contains( "<strong data-reply-target=\"author\">Fresh fragment creator</strong>", reply.Body, StringComparison.Ordinal );
            Assert.Contains( "aria-label=\"Fresh fragment booster boosted ", reply.Body, StringComparison.Ordinal );
        }
    }
}
