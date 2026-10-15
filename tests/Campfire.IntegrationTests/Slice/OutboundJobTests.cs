using System.Net;
using System.Text.Json.Nodes;
using Campfire.Application.Integrations.WebPush;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// The jobs a new message starts that reach out: Web Push to the members who follow everything
/// (<c>Room::PushMessageJob</c>, <c>WebPush::Pool</c>), and a bot's webhook, whose reply the bot posts
/// (<c>Bot::WebhookJob</c>, <c>Webhook#deliver</c>). Name lookups go to <see cref="SliceNetwork"/>, so nothing leaves the
/// machine; the webhook is a loopback server.
/// </summary>
public sealed class OutboundJobTests
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long BenderAndKevin = SliceAppFactory.Id( "bender_and_kevin" );

    private static readonly long Bender = SliceAppFactory.Id( "bender" );

    [Fact]
    public async Task A_new_message_is_pushed_to_the_other_members_who_follow_everything( )
    {
        await using var factory = new SliceAppFactory( );
        factory.Execute( $"""
            UPDATE push_subscriptions SET endpoint = 'https://updates.push.services.mozilla.com/wpush/v2/jz'
            WHERE user_id = {SliceAppFactory.Id( "jz" )}
            """ );
        factory.Execute( $"UPDATE push_subscriptions SET endpoint = 'https://web.push.apple.com/david' WHERE user_id = {SliceAppFactory.Id( "david" )}" );
        var pool = factory.Services.GetRequiredService<WebPushPool>( );

        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await david.CsrfTokenAsync( Routes.Room( Designers ) );
        var posted = await david.PostMessageAsync( Designers, token, body: "<p>Push me</p>" );
        await SliceWait.UntilAsync( ( ) => factory.Network.Lookups.Count >= 2 && pool.Pending == 0 );

        // jason and jz follow everything (fcm, mozilla); david wrote it (apple); kevin follows mentions.
        Assert.Equal( HttpStatusCode.OK, posted.Status );
        Assert.Equal( ["fcm.googleapis.com", "updates.push.services.mozilla.com"], factory.Network.Lookups.Order( StringComparer.Ordinal ) );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task A_bot_answers_a_direct_message_through_its_webhook_and_the_reply_is_broadcast( bool invalidUtf8 )
    {
        await using var factory = new SliceAppFactory( );
        await using var webhook = await LoopbackServer.StartAsync( tls: false, async httpContext =>
        {
            httpContext.Response.ContentType = "text/plain";
            var bytes = invalidUtf8 ? new byte[] { 0xC3, 0x28 } : "Bite my shiny metal"u8.ToArray( );
            await httpContext.Response.Body.WriteAsync( bytes, httpContext.RequestAborted );
        } );
        factory.Execute( $"UPDATE webhooks SET url = '{webhook.Url}/bender' WHERE user_id = {Bender}" );
        var botKey = BotApiKeys.Bender( factory );
        using var taps = await SliceTaps.OnRoomAsync( factory, BenderAndKevin );

        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.Room( BenderAndKevin ) );
        var posted = await kevin.PostMessageAsync( BenderAndKevin, token, body: "<p>Hello Bender</p>", clientMessageId: "hello-bender" );
        await SliceWait.UntilAsync( ( ) => factory.Events.Only( "broadcast append" ).Count == 2 );

        Assert.Equal( HttpStatusCode.OK, posted.Status );
        var payload = JsonNode.Parse( Assert.Single( webhook.Requests ).BodyText )!;
        var sent = SliceRows.Message( factory, "hello-bender" );
        Assert.Equal( "Kevin", payload["user"]!["name"]!.GetValue<string>( ) );
        Assert.Equal( $"/rooms/{BenderAndKevin}/{botKey}/messages", payload["room"]!["path"]!.GetValue<string>( ) );
        Assert.Equal( "Hello Bender", payload["message"]!["body"]!["plain"]!.GetValue<string>( ) );
        Assert.Equal( $"/rooms/{BenderAndKevin}/@{sent.Id}", payload["message"]!["path"]!.GetValue<string>( ) );

        var reply = factory.Scalar<long>( $"SELECT max(id) FROM messages WHERE room_id = {BenderAndKevin} AND creator_id = {Bender}" );
        Assert.True( reply > sent.Id );
        var expectedReply = invalidUtf8 ? "�(" : "Bite my shiny metal";
        var replyBody = SliceRows.MessageBody( factory, reply );
        Assert.Contains( expectedReply, replyBody, StringComparison.Ordinal );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT count(*) FROM message_search_index WHERE rowid = {reply}" ) );
    }

    [Fact]
    public async Task A_bot_can_answer_with_a_file_which_is_attached_and_processed( )
    {
        await using var factory = new SliceAppFactory( );
        var moon = SliceFiles.Moon( );
        await using var webhook = await LoopbackServer.StartAsync( tls: false, async httpContext =>
        {
            httpContext.Response.ContentType = "image/jpeg";
            await httpContext.Response.Body.WriteAsync( moon.Bytes, httpContext.RequestAborted );
        } );
        factory.Execute( $"UPDATE webhooks SET url = '{webhook.Url}/bender' WHERE user_id = {Bender}" );
        using var taps = await SliceTaps.OnRoomAsync( factory, BenderAndKevin );

        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.Room( BenderAndKevin ) );
        await kevin.PostMessageAsync( BenderAndKevin, token, body: "<p>Show me the moon</p>", clientMessageId: "show-me" );
        await SliceWait.UntilAsync( ( ) => factory.Events.Only( "broadcast append" ).Count == 2 );

        var reply = factory.Scalar<long>( $"SELECT max(id) FROM messages WHERE room_id = {BenderAndKevin} AND creator_id = {Bender}" );
        var blob = SliceRows.AttachedBlob( factory, reply );
        Assert.Equal( "attachment.jpeg", factory.Scalar<string>( $"SELECT filename FROM active_storage_blobs WHERE id = {blob.Id}" ) );
        Assert.Equal( "image/jpeg", factory.Scalar<string>( $"SELECT content_type FROM active_storage_blobs WHERE id = {blob.Id}" ) );
        Assert.Contains( "\"analyzed\":true", blob.Metadata, StringComparison.Ordinal );
        Assert.True( File.Exists( factory.App.Storage.Disk.PathFor( blob.Key ) ) );
        Assert.True( File.Exists( factory.App.Storage.Disk.PathFor( SliceRows.VariantImage( factory, blob.Id ) ) ) );
    }

    [Fact]
    public async Task A_refused_webhook_file_attachment_rolls_back_its_message_body_and_blob( )
    {
        await using var factory = new SliceAppFactory( );
        var moon = SliceFiles.Moon( );
        await using var webhook = await LoopbackServer.StartAsync( tls: false, async httpContext =>
        {
            httpContext.Response.ContentType = "image/jpeg";
            await httpContext.Response.Body.WriteAsync( moon.Bytes, httpContext.RequestAborted );
        } );
        factory.Execute( $"UPDATE webhooks SET url = '{webhook.Url}/bender' WHERE user_id = {Bender}" );
        var beforeMessages = factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE creator_id = {Bender}" );
        using var taps = await SliceTaps.OnRoomAsync( factory, BenderAndKevin );
        var beforeBlobs = factory.Scalar<long>( "SELECT count(*) FROM active_storage_blobs" );
        var beforeBodies = factory.Scalar<long>( "SELECT count(*) FROM action_text_rich_texts" );
        factory.Execute( """
            CREATE TRIGGER reject_reply_attachment BEFORE INSERT ON active_storage_attachments
            WHEN NEW.record_type = 'Message'
            BEGIN SELECT RAISE(ABORT, 'reply attachment refused'); END
            """ );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.Room( BenderAndKevin ) );

        var sent = await kevin.PostMessageAsync( BenderAndKevin, token, body: "<p>Send a refused file</p>" );
        await SliceWait.UntilAsync( ( ) => factory.App.Jobs.Webhook.Counters.Failed == 1 );

        Assert.Equal( HttpStatusCode.OK, sent.Status );
        Assert.Single( webhook.Requests );
        Assert.Equal( beforeMessages, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE creator_id = {Bender}" ) );
        Assert.Equal( beforeBlobs, factory.Scalar<long>( "SELECT count(*) FROM active_storage_blobs" ) );
        Assert.Equal( beforeBodies + 1, factory.Scalar<long>( "SELECT count(*) FROM action_text_rich_texts" ) );
        Assert.Single( factory.Events.Only( "broadcast append" ) );
    }

    [Fact]
    public async Task A_webhook_that_cannot_be_reached_fails_its_job_and_nothing_is_posted( )
    {
        await using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.Room( BenderAndKevin ) );
        var before = factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE creator_id = {Bender}" );

        // The fixture's webhook is http://example.com/bender, which SliceNetwork doesn't resolve.
        await kevin.PostMessageAsync( BenderAndKevin, token, body: "<p>Anyone there?</p>" );
        await SliceWait.UntilAsync( ( ) => factory.Network.Lookups.Contains( "example.com" ) && factory.App.Jobs.Webhook.Counters.Failed == 1 );

        Assert.Equal( before, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE creator_id = {Bender}" ) );
    }
}
