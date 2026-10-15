using System.Text.Json.Serialization;

namespace Campfire.Web.Security;

/// <summary>The JSON body of a push subscription registration: <c>{ "push_subscription": { … } }</c>.</summary>
public sealed record PushSubscriptionRequest( [property: JsonPropertyName( "push_subscription" )] PushSubscriptionBody? Subscription );
