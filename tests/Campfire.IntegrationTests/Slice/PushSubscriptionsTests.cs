using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>Users::PushSubscriptionsController</c> end to end (<c>test/controllers/users/push_subscriptions_controller_test.rb</c>):
/// the browser registers its subscription as JSON, the endpoint is validated (a permitted push service over HTTPS on
/// 443 whose host resolves to a public address, checked before the writer lease), an existing one is touched, and the
/// page lists and deletes them. The actions <c>resources</c> routes but the controller doesn't define answer 404.
/// </summary>
public sealed class PushSubscriptionsTests
{
    private const string NewEndpoint = "https://fcm.googleapis.com/fcm/send/abc123";

    private const string AttackerEndpoint = "https://attacker.example.com/steal";

    private static readonly long David = SliceAppFactory.Id( "david" );

    private static readonly long DavidChrome = SliceAppFactory.Id( "david_chrome" );

    [Fact]
    public async Task The_page_lists_the_users_subscriptions_with_their_parsed_user_agents( )
    {
        await using var factory = new SliceAppFactory( );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );

        var page = await david.GetAsync( Routes.UserPushSubscriptions );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.Contains( "<title>Push notification subscriptions</title>", page.Body, StringComparison.Ordinal );
        Assert.Contains( "<strong>Chrome 113.0.0.0 on Macintosh</strong>", page.Body, StringComparison.Ordinal );
        Assert.Contains( "<span>https://fcm.googleapis.com/fcm/send/123</span>", page.Body, StringComparison.Ordinal );
        Assert.Contains( $"action=\"/users/me/push_subscriptions/{DavidChrome}/test_notifications\"", page.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "fcm/send/567", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_new_subscription_is_created_with_the_user_agent( )
    {
        await using var factory = new SliceAppFactory( );
        factory.Network.Answer( "fcm.googleapis.com", PushSubscriber.PublicIp );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );

        var created = await SubscribeAsync( david, Subscription( NewEndpoint, "123", "456" ), ("User-Agent", "Mozilla/5.0") );

        Assert.Equal( HttpStatusCode.OK, created.Status );
        Assert.Equal( "", created.Body );
        Assert.Equal(
            "123|456|Mozilla/5.0",
            factory.Scalar<string>(
                "SELECT p256dh_key || '|' || auth_key || '|' || user_agent FROM push_subscriptions "
                + $"WHERE user_id = {David} AND endpoint = '{NewEndpoint}'" ) );
    }

    [Fact]
    public async Task An_existing_subscription_is_touched_even_when_matched_by_its_endpoint_alone( )
    {
        await using var factory = new SliceAppFactory( );
        factory.Network.Answer( "fcm.googleapis.com", PushSubscriber.PublicIp );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var count = factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" );
        var keys = factory.Scalar<string>( $"SELECT p256dh_key FROM push_subscriptions WHERE id = {DavidChrome}" );

        factory.Clock.Advance( TimeSpan.FromMinutes( 1 ) );
        var full = await SubscribeAsync( david, Subscription( "https://fcm.googleapis.com/fcm/send/123", keys!, "xxx2DtgvmLkevKRwoyahJl0efg" ) );
        var touchedAt = factory.Scalar<string>( $"SELECT updated_at FROM push_subscriptions WHERE id = {DavidChrome}" );
        factory.Clock.Advance( TimeSpan.FromMinutes( 1 ) );
        var endpointOnly = await SubscribeAsync( david, """{"push_subscription":{"endpoint":"https://fcm.googleapis.com/fcm/send/123"}}""" );

        Assert.Equal( HttpStatusCode.OK, full.Status );
        Assert.Equal( "2026-09-26 13:15:52", touchedAt );
        Assert.Equal( HttpStatusCode.OK, endpointOnly.Status );
        Assert.Equal( "2026-09-26 13:16:52", factory.Scalar<string>( $"SELECT updated_at FROM push_subscriptions WHERE id = {DavidChrome}" ) );
        Assert.Equal( count, factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" ) );
    }

    [Fact]
    public async Task Endpoints_that_are_not_permitted_or_resolve_privately_are_rejected_before_any_write( )
    {
        await using var factory = new SliceAppFactory( );
        factory.Network.Answer( "fcm.googleapis.com", "169.254.169.254" );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var count = factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" );

        var attacker = await SubscribeAsync( david, Subscription( AttackerEndpoint, "123", "456" ) );
        var plainHttp = await SubscribeAsync( david, Subscription( "http://fcm.googleapis.com/fcm/send/x", "123", "456" ) );
        var otherPort = await SubscribeAsync( david, Subscription( "https://fcm.googleapis.com:8443/fcm/send/x", "123", "456" ) );
        var metadata = await SubscribeAsync( david, Subscription( NewEndpoint, "123", "456" ) );
        var blank = await SubscribeAsync( david, """{"push_subscription":{"p256dh_key":"123"}}""" );

        Assert.All( new[] { attacker, plainHttp, otherPort, metadata, blank }, reply => Assert.Equal( HttpStatusCode.UnprocessableEntity, reply.Status ) );
        Assert.Equal( count, factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" ) );
        Assert.Equal( ["fcm.googleapis.com"], factory.Network.Lookups );
    }

    [Fact]
    public async Task A_legacy_invalid_subscription_is_not_kept_alive_by_re_registering( )
    {
        await using var factory = new SliceAppFactory( );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        factory.Execute( $"""
            INSERT INTO push_subscriptions (user_id, endpoint, p256dh_key, auth_key, created_at, updated_at)
            VALUES ({David}, '{AttackerEndpoint}', '123', '456', '2026-09-26 13:00:00', '2026-09-26 13:00:00')
            """ );
        var count = factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" );

        var again = await SubscribeAsync( david, Subscription( AttackerEndpoint, "123", "456" ) );

        Assert.Equal( HttpStatusCode.UnprocessableEntity, again.Status );
        Assert.Equal( count, factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" ) );
        Assert.Equal( "2026-09-26 13:00:00", factory.Scalar<string>( $"SELECT updated_at FROM push_subscriptions WHERE endpoint = '{AttackerEndpoint}'" ) );
    }

    [Fact]
    public async Task Destroy_removes_only_the_users_own_subscription_and_returns_to_the_list( )
    {
        await using var factory = new SliceAppFactory( );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await david.CsrfTokenAsync( Routes.UserPushSubscriptions );
        var jasons = SliceAppFactory.Id( "jason_chrome" );

        var other = await david.SubmitFormAsync( HttpMethod.Post, Routes.UserPushSubscription( jasons ), token, [("_method", "delete")] );
        var ownDeletion = await david.SubmitFormAsync( HttpMethod.Post, Routes.UserPushSubscription( DavidChrome ), token, [("_method", "delete")] );

        Assert.Equal( HttpStatusCode.Found, other.Status );
        Assert.Equal( $"{factory.BaseAddress}users/me/push_subscriptions", ownDeletion.Location );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT count(*) FROM push_subscriptions WHERE id = {jasons}" ) );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM push_subscriptions WHERE id = {DavidChrome}" ) );
    }

    [Fact]
    public async Task Wrong_methods_on_routed_actions_are_405_and_missing_actions_are_404( )
    {
        await using var factory = new SliceAppFactory( );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await david.CsrfTokenAsync( Routes.UserPushSubscriptions );

        var fresh = await david.GetAsync( "/users/me/push_subscriptions/new" );
        var show = await david.GetAsync( Routes.UserPushSubscription( DavidChrome ) );
        var edit = await david.GetAsync( $"/users/me/push_subscriptions/{DavidChrome}/edit" );
        var update = await david.SubmitFormAsync( HttpMethod.Post, Routes.UserPushSubscription( DavidChrome ), token, [("_method", "patch")] );
        var missing = await david.SubmitFormAsync(
            HttpMethod.Post,
            Routes.UserPushSubscriptions,
            token,
            [("endpoint", "https://fcm.googleapis.com/fcm/send/x")] );

        Assert.Equal( HttpStatusCode.MethodNotAllowed, fresh.Status );
        Assert.Equal( HttpStatusCode.MethodNotAllowed, show.Status );
        Assert.Equal( HttpStatusCode.NotFound, edit.Status );
        Assert.Equal( HttpStatusCode.MethodNotAllowed, update.Status );
        Assert.Equal( HttpStatusCode.UnsupportedMediaType, missing.Status );
    }

    /// <summary>
    /// The native JSON contract requires an explicit <c>push_subscription</c> object; bare JSON is rejected before lookup or write.
    /// </summary>
    [Fact]
    public async Task A_json_body_without_the_key_is_rejected_without_lookup_or_write( )
    {
        await using var factory = new SliceAppFactory( );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var count = factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" );

        var bare = await SubscribeAsync( david, """{"endpoint":"https://fcm.googleapis.com/fcm/send/x","p256dh_key":"1","auth_key":"2"}""" );

        Assert.Equal( HttpStatusCode.BadRequest, bare.Status );
        Assert.Empty( factory.Network.Lookups );
        Assert.Equal( count, factory.Scalar<long>( "SELECT count(*) FROM push_subscriptions" ) );
    }

    /// <summary>
    /// What <c>notifications_controller.js</c> posts: <paramref name="json"/> with the subscriptions page's token in
    /// <c>X-CSRF-Token</c>, and the browser's <paramref name="headers"/> (its <c>User-Agent</c>).
    /// </summary>
    private static async Task<SliceReply> SubscribeAsync( SliceClient client, string json, params (string Name, string Value)[] headers )
    {
        var token = await client.CsrfTokenAsync( Routes.UserPushSubscriptions );
        return await client.SendJsonAsync( HttpMethod.Post, Routes.UserPushSubscriptions, token, json, headers: headers );
    }

    /// <summary>The <c>push_subscription</c> JSON <c>notifications_controller.js</c> posts for a browser's subscription.</summary>
    private static string Subscription( string endpoint, string p256dhKey, string authKey ) =>
        $$$"""{"push_subscription":{"endpoint":"{{{endpoint}}}","p256dh_key":"{{{p256dhKey}}}","auth_key":"{{{authKey}}}"}}""";
}
