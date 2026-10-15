using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.Pushes;
using Campfire.Application.Serialization;

namespace Campfire.Application.Integrations.WebPush;

/// <summary>
/// <c>WebPush::Notification</c> (<c>lib/web_push/notification.rb</c>) as <c>Push::Subscription#notification</c> builds it,
/// on the enqueuing side with the badge counted there. The endpoint is resolved only when it's delivered.
/// </summary>
/// <param name="Title">The notification's title (a room name may be null).</param>
/// <param name="Body">Its body.</param>
/// <param name="Path">Where clicking it goes.</param>
/// <param name="Badge"><c>user.memberships.unread.count</c>.</param>
/// <param name="SubscriptionId">The subscription, for the invalid-subscription handler.</param>
/// <param name="Endpoint">The push service URL.</param>
/// <param name="P256dhKey">The browser's public key.</param>
/// <param name="AuthKey">The browser's auth secret.</param>
public sealed record WebPushNotification(
    string? Title, string Body, string Path, int Badge, long SubscriptionId, string? Endpoint, string? P256dhKey, string? AuthKey )
{
    /// <summary><c>Rails.application.routes.url_helpers.account_logo_path</c>.</summary>
    public const string IconPath = "/account/logo";

    /// <summary>The test notification's title.</summary>
    public const string TestTitle = "Campfire Test";

    /// <summary>
    /// <c>subscription.notification(title:, body:, path:)</c>: the badge is the subscriber's unread room count, read on
    /// <paramref name="db"/>.
    /// </summary>
    public static WebPushNotification For( CampfireDb db, PushSubscription subscription, string? title, string body, string path )
    {
        ArgumentNullException.ThrowIfNull( subscription );

        return For( subscription, title, body, path, badge: MembershipQueries.UnreadCount( db, subscription.UserId ) );
    }

    /// <summary><c>subscription.notification(title:, body:, path:)</c> with the subscriber's badge already counted.</summary>
    public static WebPushNotification For( PushSubscription subscription, string? title, string body, string path, int badge )
    {
        ArgumentNullException.ThrowIfNull( subscription );

        return new WebPushNotification(
            Title: title,
            Body: body,
            Path: path,
            Badge: badge,
            SubscriptionId: subscription.Id,
            Endpoint: subscription.Endpoint,
            P256dhKey: subscription.P256dhKey,
            AuthKey: subscription.AuthKey );
    }

    /// <summary>
    /// <c>Users::PushSubscriptions::TestNotificationsController#create</c>'s notification: "Campfire Test" with a random
    /// UUID body. <paramref name="path"/> is <c>user_push_subscriptions_url</c>, a full URL the web layer builds. The
    /// controller delivers it inline (<see cref="WebPushSender.DeliverAsync"/>) and lets errors raise, as Rails does.
    /// </summary>
    public static WebPushNotification ForTest( CampfireDb db, PushSubscription subscription, string path ) =>
        For( db, subscription, TestTitle, Guid.NewGuid( ).ToString( ), path );

    /// <summary>
    /// <c>encoded_message</c>: <c>JSON.generate title:, options: { body:, icon:, data: { path:, badge: } }</c>, the json
    /// gem's plain JSON with no HTML escaping.
    /// </summary>
    public string EncodedMessage( )
    {
        using var json = new JsonPayloadWriter( JsonEscaping.None );
        json.WriteStartObject( );
        json.WritePropertyName( "title" );
        json.WriteString( Title );
        json.WritePropertyName( "options" );
        json.WriteStartObject( );
        json.WritePropertyName( "body" );
        json.WriteString( Body );
        json.WritePropertyName( "icon" );
        json.WriteString( IconPath );
        json.WritePropertyName( "data" );
        json.WriteStartObject( );
        json.WritePropertyName( "path" );
        json.WriteString( Path );
        json.WritePropertyName( "badge" );
        json.WriteNumber( Badge );
        json.WriteEndObject( );
        json.WriteEndObject( );
        json.WriteEndObject( );
        return json.ToString( );
    }
}
