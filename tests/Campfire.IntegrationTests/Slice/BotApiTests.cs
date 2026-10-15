using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Campfire.Application.Security;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>Messages::ByBotsController</c> end to end (<c>test/controllers/messages/by_bots_controller_test.rb</c>): a bot
/// authenticated by the key in the path posts raw bodies and multipart files, pages through a room with the
/// <c>Link</c> and <c>X-Total-Count</c> headers, and edits and deletes its own messages; the status, headers and body
/// of each answer are Rails'.
/// </summary>
public sealed class BotApiTests
{
    private static readonly long Watercooler = SliceAppFactory.Id( "watercooler" );

    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long BenderAndKevin = SliceAppFactory.Id( "bender_and_kevin" );

    private static readonly long Bender = SliceAppFactory.Id( "bender" );

    [Fact]
    public async Task Create_posts_the_raw_body_and_answers_201_with_the_message_url( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        using var taps = await SliceTaps.OnRoomAsync( factory, Watercooler );

        var created = await bot.PostRawAsync( Routes.RoomBotMessages( Watercooler, BotApiKeys.Bender( factory ) ), "Hello 👋!" );

        var id = LastBotMessageId( factory );
        Assert.Equal( HttpStatusCode.Created, created.Status );
        Assert.Equal( $"{factory.BaseAddress}messages/{id}", created.Location );
        Assert.Equal( "application/json", created.ContentType );
        Assert.Equal( "", created.Body );
        Assert.Contains( "Hello 👋!", SliceRows.MessageBody( factory, id ), StringComparison.Ordinal );
        Assert.Equal( "Hello 👋!", factory.Scalar<string>( $"SELECT body FROM message_search_index WHERE rowid = {id}" ) );
        await SliceWait.UntilAsync( ( ) => factory.Events.Only( "broadcast append" ).Count == 1 );
    }

    [Fact]
    public async Task Create_rejects_invalid_utf8_before_writing_the_raw_body( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        // Bytes no string can encode to: a lead byte of a two-byte sequence followed by an ASCII byte.
        var content = new ByteArrayContent( [0xC3, 0x28] );
        content.Headers.ContentType = new MediaTypeHeaderValue( "text/plain" );
        var before = factory.Scalar<long>( "SELECT count(*) FROM messages" );

        var request = new HttpRequestMessage( HttpMethod.Post, Routes.RoomBotMessages( Watercooler, BotApiKeys.Bender( factory ) ) ) { Content = content };
        var created = await bot.SendAsync( request );

        Assert.Equal( HttpStatusCode.BadRequest, created.Status );
        Assert.Equal( before, factory.Scalar<long>( "SELECT count(*) FROM messages" ) );
    }

    [Fact]
    public async Task Create_attaches_a_top_level_multipart_file( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );

        var created = await bot.PostAttachmentAsync( Routes.RoomBotMessages( Watercooler, BotApiKeys.Bender( factory ) ), SliceFiles.Moon( ) );

        var id = LastBotMessageId( factory );
        var blob = SliceRows.AttachedBlob( factory, id );
        Assert.Equal( HttpStatusCode.Created, created.Status );
        Assert.Equal( "moon.jpg", factory.Scalar<string>( $"SELECT filename FROM active_storage_blobs WHERE id = {blob.Id}" ) );
        Assert.Contains( "\"analyzed\":true", blob.Metadata, StringComparison.Ordinal );
        Assert.True( File.Exists( SliceRows.BlobPath( factory, blob.Key ) ) );
    }

    [Fact]
    public async Task A_blank_body_without_an_attachment_is_422_and_nothing_is_created( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var messages = Routes.RoomBotMessages( Watercooler, BotApiKeys.Bender( factory ) );
        var before = factory.Scalar<long>( "SELECT count(*) FROM messages" );

        var empty = await bot.PostRawAsync( messages, "" );
        var spaces = await bot.PostRawAsync( messages, "   " );

        Assert.Equal( HttpStatusCode.UnprocessableEntity, empty.Status );
        Assert.Equal( HttpStatusCode.UnprocessableEntity, spaces.Status );
        Assert.Equal( "application/json", spaces.ContentType );
        Assert.Equal( before, factory.Scalar<long>( "SELECT count(*) FROM messages" ) );
    }

    [Fact]
    public async Task A_bot_mentioning_itself_or_writing_in_its_direct_room_triggers_no_webhook_to_itself( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var botKey = BotApiKeys.Bender( factory );
        var sgid = BenderSignedGlobalId( factory );
        var mention = $"<div>Hey <action-text-attachment sgid=\"{sgid}\" content-type=\"application/vnd.campfire.mention\"></action-text-attachment></div>";

        var mentioned = await bot.PostRawAsync( Routes.RoomBotMessages( Watercooler, botKey ), mention );
        var direct = await bot.PostRawAsync( Routes.RoomBotMessages( BenderAndKevin, botKey ), "Talking to myself again!" );

        Assert.Equal( HttpStatusCode.Created, mentioned.Status );
        Assert.Equal( HttpStatusCode.Created, direct.Status );
        Assert.Empty( factory.Events.Only( "webhook_job" ) );
    }

    [Fact]
    public async Task Invalid_bot_keys_return_401_without_redirect_and_browser_cookies_do_not_authorize_the_route( )
    {
        await using var factory = new SliceAppFactory( );
        using var anonymous = factory.Client( );
        using var browser = await factory.SignedInClientAsync( "david@37signals.com" );
        var before = factory.Scalar<long>( "SELECT count(*) FROM messages" );

        var malformed = await anonymous.PostRawAsync( Routes.RoomBotMessages( BenderAndKevin, $"{SliceAppFactory.Id( "kevin" )}-" ), "Hello 👋!" );
        var unknown = await anonymous.GetAsync( Routes.RoomBotMessages( Watercooler, "invalid-bot-key" ), "application/json" );
        var browserCookie = await browser.GetAsync( Routes.RoomBotMessages( Watercooler, "invalid-bot-key" ), "application/json" );

        Assert.Equal(
            (HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
            (malformed.Status, unknown.Status, browserCookie.Status) );
        Assert.Null( malformed.Location );
        Assert.Null( unknown.Location );
        Assert.Null( browserCookie.Location );
        Assert.Equal( before, factory.Scalar<long>( "SELECT count(*) FROM messages" ) );
    }

    [Fact]
    public async Task A_room_the_bot_is_not_in_is_404_for_index_and_create( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var messages = Routes.RoomBotMessages( Designers, BotApiKeys.Bender( factory ) );
        var before = factory.Scalar<long>( "SELECT count(*) FROM messages" );

        var index = await bot.GetAsync( messages, "application/json" );
        var create = await bot.PostRawAsync( messages, "Hello!" );

        Assert.Equal( HttpStatusCode.NotFound, index.Status );
        Assert.Equal( "", index.Body );
        Assert.Equal( HttpStatusCode.NotFound, create.Status );
        Assert.Equal( before, factory.Scalar<long>( "SELECT count(*) FROM messages" ) );
    }

    [Fact]
    public async Task The_regular_messages_index_stays_denied_to_bots( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var path = BotApiKeys.InQuery( Routes.RoomMessages( Watercooler ), BotApiKeys.Bender( factory ) );

        var denied = await bot.GetAsync( path );

        SliceAssert.SignInChallenge( factory, denied, path );
    }

    [Fact]
    public async Task Index_lists_the_last_page_in_order_with_message_details( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var messages = Routes.RoomBotMessages( Watercooler, BotApiKeys.Bender( factory ) );
        await bot.PostRawAsync( messages, "Hello from Bender!" );

        var index = await bot.GetAsync( messages, "*/*" );

        var json = JsonNode.Parse( index.Body )!.AsArray( );
        var ordered = OrderedIds( factory, Watercooler );
        var last = json[^1]!;
        Assert.Equal( HttpStatusCode.OK, index.Status );
        Assert.Equal( "application/json; charset=utf-8", index.ContentType );
        Assert.Equal( "Accept-Encoding", index.Raw( "Vary" ) );
        Assert.Equal( ordered.Count.ToString( CultureInfo.InvariantCulture ), index.Raw( "X-Total-Count" ) );
        Assert.Null( index.Raw( "Link" ) );
        Assert.Equal( ordered, MessageIds( index ) );
        Assert.Equal( "Hello from Bender!", last["body"]!["plain_text"]!.GetValue<string>( ) );
        Assert.Contains( "Hello from Bender!", last["body"]!["html"]!.GetValue<string>( ), StringComparison.Ordinal );
        Assert.StartsWith( "<div class=\"lexxy-content\">", last["body"]!["html"]!.GetValue<string>( ), StringComparison.Ordinal );
        Assert.Equal( "2026-09-26T13:14:52.000Z", last["created_at"]!.GetValue<string>( ) );
        Assert.Equal( Bender, last["creator"]!["id"]!.GetValue<long>( ) );
        Assert.Equal( "Bender Bot", last["creator"]!["name"]!.GetValue<string>( ) );
        Assert.Equal( "bot", last["creator"]!["role"]!.GetValue<string>( ) );
        Assert.StartsWith( $"{factory.BaseAddress}users/", last["creator"]!["avatar_url"]!.GetValue<string>( ), StringComparison.Ordinal );
        Assert.Equal( Watercooler, last["room"]!["id"]!.GetValue<long>( ) );
        Assert.Equal( $"{factory.BaseAddress}rooms/{Watercooler}/messages/{last["id"]!.GetValue<long>( )}", last["url"]!.GetValue<string>( ) );
    }

    [Fact]
    public async Task Index_pages_through_older_messages_with_the_link_header( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var botKey = BotApiKeys.Bender( factory );
        var messages = Routes.RoomBotMessages( Watercooler, botKey );
        var existing = OrderedIds( factory, Watercooler );
        for ( var index = 0; index < 40 - existing.Count + 1; index++ )
        {
            factory.Clock.Advance( TimeSpan.FromSeconds( 1 ) );
            Assert.Equal( HttpStatusCode.Created, (await bot.PostRawAsync( messages, $"Filler {index}" )).Status );
        }

        var first = await bot.GetAsync( messages, "application/json" );
        var link = first.Raw( "Link" )!;
        var next = link[1..link.IndexOf( '>', StringComparison.Ordinal )];
        var second = await bot.GetAsync( next, "application/json" );

        var oldest = existing[0];
        Assert.Equal( 40, JsonNode.Parse( first.Body )!.AsArray( ).Count );
        Assert.Equal( "41", first.Raw( "X-Total-Count" ) );
        Assert.Equal( $"<{factory.BaseAddress}rooms/{Watercooler}/{botKey}/messages?before={OrderedIds( factory, Watercooler )[1]}>; rel=\"next\"", link );
        Assert.Equal( [oldest], MessageIds( second ) );
        Assert.Null( second.Raw( "Link" ) );
    }

    [Fact]
    public async Task Index_pages_newer_messages_with_after_and_an_empty_room_is_an_empty_array( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var botKey = BotApiKeys.Bender( factory );
        var watercoolerMessages = Routes.RoomBotMessages( Watercooler, botKey );
        var tenth = SliceAppFactory.Id( "tenth" );
        var ordered = OrderedIds( factory, Watercooler );

        var after = await bot.GetAsync( $"{watercoolerMessages}?after={tenth}", "application/json" );
        var empty = await bot.GetAsync( Routes.RoomBotMessages( BenderAndKevin, botKey ), "application/json" );
        var missing = await bot.GetAsync( $"{watercoolerMessages}?before=0", "application/json" );

        Assert.Equal( ordered.SkipWhile( id => id != tenth ).Skip( 1 ), MessageIds( after ) );
        Assert.Null( after.Raw( "Link" ) );
        Assert.Equal( "[]", empty.Body );
        Assert.Equal( "0", empty.Raw( "X-Total-Count" ) );
        Assert.Null( empty.Raw( "Link" ) );
        Assert.Equal( HttpStatusCode.NotFound, missing.Status );
        Assert.Equal( 404, JsonNode.Parse( missing.Body )!["status"]!.GetValue<int>( ) );
        Assert.Equal( "Not Found", JsonNode.Parse( missing.Body )!["title"]!.GetValue<string>( ) );
    }

    [Fact]
    public async Task Update_edits_the_bots_own_message_broadcasts_it_and_answers_its_json( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var id = await PostBotMessageAsync( factory, bot, "Deploying..." );
        using var taps = await SliceTaps.OnRoomAsync( factory, Watercooler );
        var before = factory.Scalar<long>( "SELECT count(*) FROM messages" );

        var updated = await bot.SendRawAsync( HttpMethod.Patch, Routes.RoomBotMessage( Watercooler, BotApiKeys.Bender( factory ), id ), "Deployed 🚀!" );

        var json = JsonNode.Parse( updated.Body )!;
        Assert.Equal( HttpStatusCode.OK, updated.Status );
        Assert.Equal( "application/json; charset=utf-8", updated.ContentType );
        Assert.Equal( before, factory.Scalar<long>( "SELECT count(*) FROM messages" ) );
        Assert.Equal( id, json["id"]!.GetValue<long>( ) );
        Assert.Equal( "Deployed 🚀!", json["body"]!["plain_text"]!.GetValue<string>( ) );
        Assert.Equal( Bender, json["creator"]!["id"]!.GetValue<long>( ) );
        Assert.Equal( $"{factory.BaseAddress}rooms/{Watercooler}/messages/{id}", json["url"]!.GetValue<string>( ) );
        Assert.Equal( "Deployed 🚀!", factory.Scalar<string>( $"SELECT body FROM message_search_index WHERE rowid = {id}" ) );
        await SliceWait.UntilAsync( ( ) => factory.Events.Only( "broadcast replace" ).Count == 1 );
    }

    [Fact]
    public async Task A_bot_can_neither_update_nor_destroy_a_message_it_did_not_write( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var botKey = BotApiKeys.Bender( factory );
        var fourth = SliceAppFactory.Id( "fourth" );
        var original = SliceRows.MessageBody( factory, fourth );

        var update = await bot.SendRawAsync( HttpMethod.Patch, Routes.RoomBotMessage( Watercooler, botKey, fourth ), "Hijacked!" );
        var destroy = await bot.SendRawAsync( HttpMethod.Delete, Routes.RoomBotMessage( Watercooler, botKey, fourth ), "" );
        var elsewhere = await bot.SendRawAsync( HttpMethod.Patch, Routes.RoomBotMessage( Designers, botKey, SliceAppFactory.Id( "first" ) ), "Hijacked!" );
        var asJz = await bot.SendRawAsync( HttpMethod.Patch, Routes.RoomBotMessage( Watercooler, $"{SliceAppFactory.Id( "jz" )}-", fourth ), "Hijacked!" );

        Assert.Equal( HttpStatusCode.Forbidden, update.Status );
        Assert.Equal( HttpStatusCode.Forbidden, destroy.Status );
        Assert.Equal( HttpStatusCode.NotFound, elsewhere.Status );
        Assert.Equal( HttpStatusCode.Unauthorized, asJz.Status );
        Assert.Null( asJz.Location );
        Assert.Equal( original, SliceRows.MessageBody( factory, fourth ) );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE id = {fourth}" ) );
    }

    [Fact]
    public async Task Destroy_removes_the_bots_message_and_broadcasts_the_removal( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var id = await PostBotMessageAsync( factory, bot, "Deploying..." );
        using var taps = await SliceTaps.OnRoomAsync( factory, Watercooler );

        var destroyed = await bot.SendRawAsync( HttpMethod.Delete, Routes.RoomBotMessage( Watercooler, BotApiKeys.Bender( factory ), id ), "" );

        Assert.Equal( HttpStatusCode.NoContent, destroyed.Status );
        Assert.Null( destroyed.ContentType );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE id = {id}" ) );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM message_search_index WHERE rowid = {id}" ) );
        await SliceWait.UntilAsync( ( ) => factory.Events.Only( "broadcast remove" ).Count == 1 );
    }

    private static async Task<long> PostBotMessageAsync( SliceAppFactory factory, SliceClient bot, string body )
    {
        var created = await bot.PostRawAsync( Routes.RoomBotMessages( Watercooler, BotApiKeys.Bender( factory ) ), body );
        Assert.Equal( HttpStatusCode.Created, created.Status );
        return LastBotMessageId( factory );
    }

    /// <summary>The newest message Bender wrote.</summary>
    private static long LastBotMessageId( SliceAppFactory factory ) =>
        factory.Scalar<long>( $"SELECT max(id) FROM messages WHERE creator_id = {Bender}" );

    /// <summary>The <c>id</c>s of the messages in an index answer, in order.</summary>
    private static IEnumerable<long> MessageIds( SliceReply index ) =>
        JsonNode.Parse( index.Body )!.AsArray( ).Select( message => message!["id"]!.GetValue<long>( ) );

    /// <summary><c>room.messages.ordered</c>: by creation, then id.</summary>
    private static List<long> OrderedIds( SliceAppFactory factory, long roomId )
    {
        using var connection = factory.OpenSideConnection( );
        using var command = connection.CreateCommand( );
        command.CommandText = $"SELECT id FROM messages WHERE room_id = {roomId} ORDER BY created_at, id";
        using var reader = command.ExecuteReader( );
        var ids = new List<long>( );
        while ( reader.Read( ) )
        {
            ids.Add( reader.GetInt64( 0 ) );
        }

        return ids;
    }

    private static string BenderSignedGlobalId( SliceAppFactory factory ) =>
        factory.App.SignedGlobalIds.AttachableSignedGlobalId( GlobalId.For( "User", Bender ) );
}
