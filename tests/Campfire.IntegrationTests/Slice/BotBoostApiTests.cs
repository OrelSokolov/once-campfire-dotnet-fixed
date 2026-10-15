using System.Net;
using System.Text.Json.Nodes;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>Messages::Boosts::ByBotsController</c> end to end: a bot boosts a message in one of its rooms with the raw
/// request body (201 and <c>messages/boosts/by_bots/show.json</c>), and removes its own boost (204); both broadcast.
/// </summary>
public sealed class BotBoostApiTests
{
    private static readonly long Watercooler = SliceAppFactory.Id( "watercooler" );

    private static readonly long Bender = SliceAppFactory.Id( "bender" );

    private static readonly long Thirteenth = SliceAppFactory.Id( "thirteenth" );

    [Fact]
    public async Task Create_boosts_with_the_raw_body_broadcasts_and_answers_201_with_the_boost( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        using var taps = await SliceTaps.OnRoomAsync( factory, Watercooler );

        var created = await bot.PostRawAsync( Routes.RoomBotMessageBoosts( Watercooler, BotApiKeys.Bender( factory ), Thirteenth ), "🤖" );

        var json = JsonNode.Parse( created.Body )!;
        var id = factory.Scalar<long>( $"SELECT max(id) FROM boosts WHERE booster_id = {Bender}" );
        Assert.Equal( HttpStatusCode.Created, created.Status );
        Assert.Equal( "application/json; charset=utf-8", created.ContentType );
        Assert.Equal( id, json["id"]!.GetValue<long>( ) );
        Assert.Equal( "🤖", json["content"]!.GetValue<string>( ) );
        Assert.Equal( "2026-09-26T13:14:52.000Z", json["created_at"]!.GetValue<string>( ) );
        Assert.Equal( Bender, json["booster"]!["id"]!.GetValue<long>( ) );
        Assert.Equal( "bot", json["booster"]!["role"]!.GetValue<string>( ) );
        Assert.Equal( Thirteenth, json["message"]!["id"]!.GetValue<long>( ) );
        Assert.Equal( $"{factory.BaseAddress}rooms/{Watercooler}/messages/{Thirteenth}", json["message"]!["url"]!.GetValue<string>( ) );
        Assert.Equal( "🤖", factory.Scalar<string>( $"SELECT content FROM boosts WHERE id = {id}" ) );
        await SliceWait.UntilAsync( ( ) => factory.Events.Only( "broadcast append" ).Count == 1 );
    }

    [Fact]
    public async Task A_blank_boost_is_422_and_a_message_outside_the_bots_rooms_is_404( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var botKey = BotApiKeys.Bender( factory );
        var first = SliceAppFactory.Id( "first" );
        var before = factory.Scalar<long>( "SELECT count(*) FROM boosts" );

        var blank = await bot.PostRawAsync( Routes.RoomBotMessageBoosts( Watercooler, botKey, Thirteenth ), " \n" );
        var otherRoom = await bot.PostRawAsync( Routes.RoomBotMessageBoosts( SliceAppFactory.Id( "designers" ), botKey, first ), "👍" );
        var notInRoom = await bot.PostRawAsync( Routes.RoomBotMessageBoosts( Watercooler, botKey, first ), "👍" );

        Assert.Equal( HttpStatusCode.UnprocessableEntity, blank.Status );
        Assert.Equal( HttpStatusCode.NotFound, otherRoom.Status );
        Assert.Equal( HttpStatusCode.NotFound, notInRoom.Status );
        Assert.Equal( "", notInRoom.Body );
        Assert.Equal( before, factory.Scalar<long>( "SELECT count(*) FROM boosts" ) );
    }

    [Fact]
    public async Task Destroy_removes_the_bots_own_boost_only( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var botKey = BotApiKeys.Bender( factory );
        var david = SliceAppFactory.Id( "david" );
        Assert.Equal( HttpStatusCode.Created, (await bot.PostRawAsync( Routes.RoomBotMessageBoosts( Watercooler, botKey, Thirteenth ), "🤖" )).Status );
        var botBoost = factory.Scalar<long>( $"SELECT max(id) FROM boosts WHERE booster_id = {Bender}" );
        factory.Execute( $"""
            INSERT INTO boosts (booster_id, content, created_at, message_id, updated_at)
            VALUES ({david}, 'mine', '2026-09-26 13:00:00', {Thirteenth}, '2026-09-26 13:00:00')
            """ );
        var davids = factory.Scalar<long>( $"SELECT max(id) FROM boosts WHERE booster_id = {david}" );
        using var taps = await SliceTaps.OnRoomAsync( factory, Watercooler );

        var other = await bot.SendRawAsync( HttpMethod.Delete, Routes.RoomBotMessageBoost( Watercooler, botKey, Thirteenth, davids ), "" );
        var destroyed = await bot.SendRawAsync( HttpMethod.Delete, Routes.RoomBotMessageBoost( Watercooler, botKey, Thirteenth, botBoost ), "" );

        Assert.Equal( HttpStatusCode.NotFound, other.Status );
        Assert.Equal( HttpStatusCode.NoContent, destroyed.Status );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM boosts WHERE id = {botBoost}" ) );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT count(*) FROM boosts WHERE id = {davids}" ) );
        await SliceWait.UntilAsync( ( ) => factory.Events.Only( "broadcast remove" ).Count == 1 );
    }
}
