using Campfire.Application.Bots;
using Campfire.Application.Data;
using Campfire.Application.Data.Hooks;
using Campfire.Application.Messages;
using Campfire.Application.RichText;
using Campfire.Application.Rooms;
using Campfire.Application.Ruby;
using Campfire.Application.Serialization;
using Campfire.Application.Users;

namespace Campfire.Application.Integrations.Webhooks;

/// <summary>
/// <c>Bot::WebhookJob#perform(bot, message)</c> up to the HTTP request: <c>bot.deliver_webhook(message)</c> reads the
/// bot's webhook and builds <c>Webhook#payload(message)</c> (<c>app/models/webhook.rb</c>), a <c>Hash#to_json</c> with
/// ActiveSupport's escaping.
/// </summary>
public static class WebhookPayload
{
    /// <summary>
    /// The delivery for <paramref name="messageId"/> to <paramref name="botId"/>'s webhook. A bot without a webhook
    /// raises as Rails' <c>nil.deliver</c> does.
    /// </summary>
    public static WebhookRequest Build( CampfireDb db, IMessageBodyText bodyText, RichTextContext richText, long botId, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( bodyText );
        ArgumentNullException.ThrowIfNull( richText );

        var bot = UserQueries.Find( db, botId );
        var message = MessageQueries.Find( db, messageId );
        var webhook = BotOperations.FindWebhookByBotId( db, botId );
        if ( webhook is null )
        {
            throw new WebhookDeliveryException( RubyErrorClasses.NoMethodError, "undefined method 'deliver' for nil (the bot has no webhook)" );
        }

        var creator = UserQueries.Find( db, message.CreatorId );
        var room = RoomQueries.Find( db, message.RoomId );
        var stored = MessageBodies.Body( db, message.Id );
        var plain = RichTextPipeline.WithoutRecipientMentions( MessageBodies.PlainText( db, bodyText, message.Id ), bot.Name );
        var html = stored is null ? null : RichTextPipeline.CanonicalHtml( stored, richText );
        var data = new WebhookPayloadData( creator.Id, creator.Name, room.Id, room.Name, bot.BotKey, message.Id, html, plain );
        return new WebhookRequest( botId, room.Id, webhook.Url, Encode( data ) );
    }

    /// <summary>
    /// The JSON: <c>{ user: { id, name }, room: { id, name, path: room_bot_messages_path }, message: { id, body: { html,
    /// plain }, path: room_at_message_path } }.to_json</c>.
    /// </summary>
    public static string Encode( WebhookPayloadData data )
    {
        ArgumentNullException.ThrowIfNull( data );

        using var json = new JsonPayloadWriter( JsonEscaping.HtmlEntities );
        json.WriteStartObject( );
        json.WritePropertyName( "user" );
        json.WriteStartObject( );
        json.WritePropertyName( "id" );
        json.WriteNumber( data.UserId );
        json.WritePropertyName( "name" );
        json.WriteString( data.UserName );
        json.WriteEndObject( );

        json.WritePropertyName( "room" );
        json.WriteStartObject( );
        json.WritePropertyName( "id" );
        json.WriteNumber( data.RoomId );
        json.WritePropertyName( "name" );
        json.WriteString( data.RoomName );
        json.WritePropertyName( "path" );
        json.WriteString( $"/rooms/{data.RoomId}/{data.BotKey}/messages" );
        json.WriteEndObject( );

        json.WritePropertyName( "message" );
        json.WriteStartObject( );
        json.WritePropertyName( "id" );
        json.WriteNumber( data.MessageId );
        json.WritePropertyName( "body" );
        json.WriteStartObject( );
        json.WritePropertyName( "html" );
        json.WriteString( data.Html );
        json.WritePropertyName( "plain" );
        json.WriteString( data.Plain );
        json.WriteEndObject( );
        json.WritePropertyName( "path" );
        json.WriteString( $"/rooms/{data.RoomId}/@{data.MessageId}" );
        json.WriteEndObject( );
        json.WriteEndObject( );
        return json.ToString( );
    }
}
