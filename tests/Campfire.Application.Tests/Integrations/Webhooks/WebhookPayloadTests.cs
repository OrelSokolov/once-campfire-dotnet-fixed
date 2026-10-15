using System.Text.Json.Nodes;

using Campfire.Application.Integrations.Webhooks;
using Campfire.Application.RichText;
using Campfire.Application.Tests.Support;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Integrations.Webhooks;

/// <summary>
/// <c>Webhook#payload</c>: byte for byte against <c>vectors/json/webhook_payload.json</c> (ActiveSupport escaping of a
/// hostile room name and a mention's HTML), and built from the database as <c>test/models/webhook_test.rb</c> expects.
/// </summary>
public sealed class WebhookPayloadTests
{
    [Fact]
    public void Encodes_the_payload_as_rails_does( )
    {
        var expected = RepositoryPaths.ReadVectorText( "json", "webhook_payload.json" );
        var parsed = JsonNode.Parse( expected )!;
        var data = new WebhookPayloadData(
            parsed["user"]!["id"]!.GetValue<long>( ),
            parsed["user"]!["name"]!.GetValue<string>( ),
            parsed["room"]!["id"]!.GetValue<long>( ),
            parsed["room"]!["name"]!.GetValue<string>( ),
            "773523960-JsonVectorBot1",
            parsed["message"]!["id"]!.GetValue<long>( ),
            parsed["message"]!["body"]!["html"]!.GetValue<string>( ),
            parsed["message"]!["body"]!["plain"]!.GetValue<string>( ) );

        Assert.Equal( expected, WebhookPayload.Encode( data ) );
    }

    [Fact]
    public void A_direct_room_and_a_body_less_message_encode_as_null( )
    {
        var json = WebhookPayload.Encode( new WebhookPayloadData( 1, "David", 2, null, "3-abc", 4, null, "moon.jpg" ) );

        Assert.Equal(
            "{\"user\":{\"id\":1,\"name\":\"David\"},\"room\":{\"id\":2,\"name\":null,\"path\":\"/rooms/2/3-abc/messages\"},"
            + "\"message\":{\"id\":4,\"body\":{\"html\":null,\"plain\":\"moon.jpg\"},\"path\":\"/rooms/2/@4\"}}",
            json );
    }

    [Fact]
    public async Task Builds_the_payload_for_a_bots_webhook( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bender = TestDatabase.Id( "bender" );
        var first = TestDatabase.Id( "first" );
        var designers = TestDatabase.Id( "designers" );
        var botKey = await testDatabase.ReadAsync( db => UserQueries.Find( db, bender ).BotKey );

        var request = await testDatabase.ReadAsync( db => WebhookPayload.Build( db, testDatabase.BodyText, PlainContext, bender, first ) );

        Assert.Equal( (bender, designers, "http://example.com/bender"), (request.BotId, request.RoomId, request.Url) );
        var payload = JsonNode.Parse( request.Payload )!;
        Assert.Equal( TestDatabase.Id( "jason" ), payload["user"]!["id"]!.GetValue<long>( ) );
        Assert.Equal( "Jason", payload["user"]!["name"]!.GetValue<string>( ) );
        Assert.Equal( "Designers", payload["room"]!["name"]!.GetValue<string>( ) );
        Assert.Equal( $"/rooms/{designers}/{botKey}/messages", payload["room"]!["path"]!.GetValue<string>( ) );
        Assert.Equal( "First post!", payload["message"]!["body"]!["html"]!.GetValue<string>( ) );
        Assert.Equal( "First post!", payload["message"]!["body"]!["plain"]!.GetValue<string>( ) );
        Assert.Equal( $"/rooms/{designers}/@{first}", payload["message"]!["path"]!.GetValue<string>( ) );
    }

    [Fact]
    public async Task Leaves_out_mentions_of_the_bot_itself( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bender = TestDatabase.Id( "bender" );
        var message = await testDatabase.PostMessageAsync( "designers", "david", $"{BasicBodyText.MentionAttachmentFor( bender )} what's up?" );

        var request = await testDatabase.ReadAsync( db => WebhookPayload.Build( db, testDatabase.BodyText, PlainContext, bender, message.Id ) );

        Assert.Equal( "what's up?", JsonNode.Parse( request.Payload )!["message"]!["body"]!["plain"]!.GetValue<string>( ) );
    }

    [Fact]
    public async Task A_bot_without_a_webhook_raises( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bender = TestDatabase.Id( "bender" );
        await testDatabase.ExecuteAsync( "DELETE FROM webhooks WHERE user_id = @p0", bender );

        var error = await Assert.ThrowsAsync<WebhookDeliveryException>( ( ) => testDatabase.ReadAsync( db => WebhookPayload.Build(
            db,
            testDatabase.BodyText,
            PlainContext,
            bender,
            TestDatabase.Id( "first" ) ) ) );

        Assert.Equal( "NoMethodError", error.RubyClass );
    }

    /// <summary>Rich text context for bodies without attachments: nothing to resolve.</summary>
    private static RichTextContext PlainContext { get; } = new( new NoAttachables( ), "campfire.test" );

    private sealed class NoAttachables : IAttachableResolver
    {
        public SignedAttachableLookup LocateSigned( string signedGlobalId ) => SignedAttachableLookup.Invalid;

    }
}
