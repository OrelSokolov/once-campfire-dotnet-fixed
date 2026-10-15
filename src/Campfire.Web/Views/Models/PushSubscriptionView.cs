namespace Campfire.Web.Views.Models;

/// <summary>A <c>Push::Subscription</c> with its user agent parsed (<c>UserAgent.parse</c>).</summary>
public sealed record PushSubscriptionView( long Id, string Endpoint, string Browser, string Version, string Platform );
