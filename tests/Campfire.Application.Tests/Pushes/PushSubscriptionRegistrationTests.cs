using Campfire.Application.Data;
using Campfire.Application.Pushes;
using Campfire.Application.Tests.Support;

namespace Campfire.Application.Tests.Pushes;

/// <summary>
/// <c>Users::PushSubscriptionsController#create</c>'s write (<see cref="PushSubscriptionRegistration"/>):
/// <c>find_by</c> on the keys given, <c>valid?</c> and <c>touch</c> or <c>create</c>, and only endpoints validated before
/// the lease are ever kept.
/// </summary>
public sealed class PushSubscriptionRegistrationTests
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/123";

    private static readonly PushSubscriptionParams DavidsChrome = new(
        Endpoint, "123-RIXcMgkdjhRnFZaYjjGvo00dydRQbCpQTuXFjLaCPSE7ofxi19awgGc3Doqa1RmYQqsbQDfQTifFZgc", "xxx2DtgvmLkevKRwoyahJl0efg" );

    [Fact]
    public async Task Find_by_matches_only_the_keys_given_and_nil_matches_null( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var david = TestDatabase.Id( "david" );
        var chrome = TestDatabase.Id( "david_chrome" );

        var full = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.FindBy( db, david, DavidsChrome ) );
        var endpointOnly = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.FindBy(
            db,
            david,
            new PushSubscriptionParams( Endpoint, default, default ) ) );
        var none = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.FindBy(
            db,
            david,
            new PushSubscriptionParams( default, default, default ) ) );
        var nullKey = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.FindBy(
            db,
            david,
            new PushSubscriptionParams( Endpoint, new Assigned<string?>( null ), default ) ) );
        var jasons = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.FindBy( db, TestDatabase.Id( "jason" ), DavidsChrome ) );

        Assert.Equal( chrome, full?.Id );
        Assert.Equal( chrome, endpointOnly?.Id );
        Assert.Equal( chrome, none?.Id );
        Assert.Null( nullKey );
        Assert.Null( jasons );
    }

    [Fact]
    public async Task Register_touches_a_valid_match_creates_a_valid_new_one_and_rejects_the_rest( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var david = TestDatabase.Id( "david" );
        var chrome = TestDatabase.Id( "david_chrome" );
        var before = await testDatabase.CountAsync( "push_subscriptions" );
        var validated = new HashSet<string?>( StringComparer.Ordinal ) { Endpoint, "https://fcm.googleapis.com/fcm/send/new" };

        testDatabase.Clock.TravelTo( testDatabase.Clock.Now.AddMinutes( 1 ) );
        var touched = await testDatabase.WriteAsync( scope => PushSubscriptionRegistration.Register( scope, david, DavidsChrome, "ua", validated.Contains ) );
        var created = await testDatabase.WriteAsync( scope => PushSubscriptionRegistration.Register(
            scope, david, new PushSubscriptionParams( "https://fcm.googleapis.com/fcm/send/new", "k", "a" ), "Mozilla/5.0", validated.Contains ) );
        var unvalidated = await testDatabase.WriteAsync( scope => PushSubscriptionRegistration.Register(
            scope, david, new PushSubscriptionParams( "https://fcm.googleapis.com/fcm/send/other", "k", "a" ), "ua", validated.Contains ) );
        var blank = await testDatabase.WriteAsync( scope => PushSubscriptionRegistration.Register(
            scope,
            TestDatabase.Id( "kevin" ),
            new PushSubscriptionParams( default, "k", default ),
            "ua",
            validated.Contains ) );

        var subscriptions = await testDatabase.ReadAsync( db => PushSubscriptionOperations.ForUser( db, david ) );
        Assert.Equal(
            [SubscribeOutcome.Subscribed, SubscribeOutcome.Subscribed, SubscribeOutcome.Invalid, SubscribeOutcome.Invalid],
            [touched, created, unvalidated, blank] );
        Assert.Equal( before + 1, await testDatabase.CountAsync( "push_subscriptions" ) );
        Assert.Equal( testDatabase.Clock.Now, subscriptions.Single( subscription => subscription.Id == chrome ).UpdatedAt );
        Assert.Contains(
            subscriptions,
            subscription =>
                subscription is { Endpoint: "https://fcm.googleapis.com/fcm/send/new", P256dhKey: "k", AuthKey: "a", UserAgent: "Mozilla/5.0" } );
    }

    [Fact]
    public async Task The_endpoints_to_validate_are_the_matching_rows_and_the_new_one( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var david = TestDatabase.Id( "david" );

        var same = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.EndpointsToValidate( db, david, DavidsChrome ) );
        var keysOnly = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.EndpointsToValidate(
            db,
            david,
            new PushSubscriptionParams( default, DavidsChrome.P256dhKey, default ) ) );
        var fresh = await testDatabase.ReadAsync( db => PushSubscriptionRegistration.EndpointsToValidate(
            db,
            david,
            new PushSubscriptionParams( "https://fcm.googleapis.com/x", "k", "a" ) ) );

        Assert.Equal( [Endpoint], same );
        Assert.Equal( [Endpoint, null], keysOnly );
        Assert.Equal( ["https://fcm.googleapis.com/x"], fresh );
    }

    [Fact]
    public async Task Find_for_user_scopes_to_the_users_own_subscriptions( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        Assert.NotNull( await testDatabase.ReadAsync( db => PushSubscriptionRegistration.FindByIdForUser(
            db,
            TestDatabase.Id( "david" ),
            TestDatabase.Id( "david_chrome" ) ) ) );
        Assert.Null( await testDatabase.ReadAsync( db => PushSubscriptionRegistration.FindByIdForUser(
            db,
            TestDatabase.Id( "david" ),
            TestDatabase.Id( "jason_chrome" ) ) ) );
    }
}
