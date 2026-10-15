using System.Net;
using System.Text.Json.Nodes;
using Campfire.Application.Data;
using Campfire.Application.Rooms;
using Campfire.Application.Users;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Channels;

/// <summary>
/// Revoked authority, for every channel a page subscribes to: losing a membership and signing
/// out disconnect with <c>reconnect: true</c> (<c>reset_remote_connections</c>); deactivation and bans with
/// <c>reconnect: false</c>, after the transaction commits. Broadcasts race the
/// revocation the whole time: frames already queued go out first, then the disconnect, then nothing.
/// </summary>
public sealed class RevocationTests
{
    public enum Revocation
    {
        MembershipRemoved,
        SignedOut,
        Deactivated,
        Banned,
    }

    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long Jz = SliceAppFactory.Id( "jz" );

    [Theory]
    [InlineData( Revocation.MembershipRemoved )]
    [InlineData( Revocation.SignedOut )]
    [InlineData( Revocation.Deactivated )]
    [InlineData( Revocation.Banned )]
    public async Task Every_channel_stops_at_the_revocation_while_broadcasts_race_it( Revocation revocation )
    {
        await using var factory = new SliceAppFactory( );
        using var jz = await factory.SignedInClientAsync( "jz@37signals.com" );
        var token = await jz.CsrfTokenAsync( Routes.Room( Designers ) );
        var cookie = jz.CookieHeader;
        if ( revocation == Revocation.Banned )
        {
            // Bans need public session addresses; the test client's is loopback (a ban on it answers 422).
            factory.Execute( "UPDATE sessions SET ip_address = '8.8.8.8'" );
        }

        var subscriptions = await PageSubscriptions.ForAsync( factory.App, Designers, Jz );
        await using var cable = await Realtime.ConnectAsync( jz );
        foreach ( var subscription in subscriptions.All )
        {
            await Realtime.ExpectConfirmedAsync( cable, subscription.Identifier );
        }

        await Realtime.ExpectRejectedAsync( cable, subscriptions.GuardedTurbo );

        var broadcasts = new NumberedBroadcasts( factory.App.Cable, [.. subscriptions.Broadcastings] );
        using var stop = new CancellationTokenSource( );
        var racing = broadcasts.RunAsync( stop.Token );
        for ( var index = 0; index < 12; index++ )
        {
            broadcasts.Round( ); // queued behind the paced ones when the revocation lands
        }

        await RevokeAsync( factory, jz, token, revocation );
        var last = broadcasts.Last;
        for ( var index = 0; index < 12; index++ )
        {
            broadcasts.Round( );
        }

        await stop.CancelAsync( );
        await racing;

        var frames = (await cable.CollectAsync( TimeSpan.FromSeconds( 2 ) )).Select( frame => frame.ToString( ) ).ToList( );
        var reconnect = revocation is Revocation.MembershipRemoved or Revocation.SignedOut;
        var disconnect = frames.IndexOf( Realtime.RemoteDisconnect( reconnect ) );
        Assert.True( disconnect >= 0, $"no disconnect in {string.Join( " | ", frames.TakeLast( 5 ) )}" );
        Assert.Equal( [Realtime.RemoteDisconnect( reconnect ), Realtime.NormalClose, "end"], frames[disconnect..] );

        var delivered = frames[..disconnect].Select( Realtime.Delivery ).ToList( );
        var identifiers = subscriptions.All
            .Where( subscription => subscription.Broadcasting is not null )
            .Select( subscription => subscription.Identifier )
            .ToHashSet( );
        Assert.NotEmpty( delivered );
        Assert.All( delivered, delivery => Assert.Contains( delivery.Identifier, identifiers ) );
        Assert.All(
            delivered,
            delivery => Assert.True(
                NumberedBroadcasts.SequenceNumber( delivery.Message ) <= last,
                $"delivered {delivery.Message} after the revocation (last before it: {last})" ) );

        await AssertReconnectAsync( factory, cookie, subscriptions, revocation );
    }

    [Theory]
    [InlineData( Revocation.Deactivated )]
    [InlineData( Revocation.Banned )]
    public async Task A_connection_opened_after_the_commit_is_rejected( Revocation revocation )
    {
        await using var factory = new SliceAppFactory( );
        using var jz = await factory.SignedInClientAsync( "jz@37signals.com" );
        factory.Execute( "UPDATE sessions SET ip_address = '8.8.8.8'" );
        await using var before = await Realtime.ConnectAsync( jz );

        // The disconnect callback runs after commit, so a connection opened by this hook sees the revoked session.
        Task<CableTestClient>? between = null;
        factory.Faults.AfterDisconnect = ( _, _ ) =>
        {
            between = Realtime.ConnectWithCookieAsync( factory, jz.CookieHeader );

            // The hook is synchronous, so it spins rather than awaiting the handshake.
            var handshakeFinished = SpinWait.SpinUntil( ( ) => between.IsCompleted, TimeSpan.FromSeconds( 10 ) );
            Assert.True( handshakeFinished, "the in-between connection never finished its handshake" );
        };

        await RevokeAsync( factory, jz, signOutToken: null, revocation );

        Assert.NotNull( between );
        await using var inBetween = await between;
        Assert.Equal( Realtime.RemoteDisconnect( reconnect: false ), await before.NextTextAsync( ) );
        Assert.Equal( Realtime.Unauthorized, await inBetween.NextTextAsync( ) );
        Assert.Equal( Realtime.NormalClose, (await inBetween.NextAsync( )).ToString( ) );
        Assert.Equal( ["disconnect user " + Jz + " reconnect=false"], factory.Events.Only( "disconnect user" ) );
    }

    [Fact]
    public async Task Signing_out_resets_the_users_other_sessions_which_reconnect( )
    {
        await using var factory = new SliceAppFactory( );
        using var phone = await factory.SignedInClientAsync( "jz@37signals.com" );
        using var laptop = await factory.SignedInClientAsync( "jz@37signals.com" );
        var token = await laptop.CsrfTokenAsync( Routes.Room( Designers ) );
        await using var phoneCable = await Realtime.ConnectAsync( phone );

        await RevokeAsync( factory, laptop, token, Revocation.SignedOut );

        Assert.Equal( Realtime.RemoteDisconnect( reconnect: true ), await phoneCable.NextTextAsync( ) );
        await using var again = await Realtime.ConnectAsync( phone );
    }

    [Fact]
    public async Task A_ban_that_fails_on_a_private_session_address_leaves_the_sockets_alone( )
    {
        await using var factory = new SliceAppFactory( );
        using var jz = await factory.SignedInClientAsync( "jz@37signals.com" );
        var subscriptions = await PageSubscriptions.ForAsync( factory.App, Designers, Jz );
        await using var cable = await Realtime.ConnectAsync( jz );
        var unreads = subscriptions.All.Single( subscription => subscription.Name == "UnreadRoomsChannel" );
        await Realtime.ExpectConfirmedAsync( cable, unreads.Identifier );

        await Assert.ThrowsAsync<RecordInvalidException>(
            ( ) => factory.App.Database.WriteAsync( scope => UserBans.Ban( scope, Jz ), CancellationToken.None ) );

        factory.App.Cable.Broadcast( unreads.Broadcasting!, new JsonObject { ["roomId"] = Designers } );
        Assert.Equal( unreads.Identifier, Realtime.Delivery( await cable.NextTextAsync( ) ).Identifier );
        Assert.Empty( factory.Events.Only( "disconnect user" ) );
    }

    /// <summary>
    /// Revokes jz's authority the way the app does (signing out through <paramref name="jz"/>, with its page's
    /// <paramref name="signOutToken"/>); returns once the revocation has taken effect.
    /// </summary>
    private static async Task RevokeAsync( SliceAppFactory factory, SliceClient jz, string? signOutToken, Revocation revocation )
    {
        var database = factory.App.Database;
        switch ( revocation )
        {
            case Revocation.MembershipRemoved:
                // Rooms::ClosedsController#update → room.memberships.revise(revoked:) → revoke_from.
                await database.WriteAsync( scope => RoomMemberships.RevokeFrom( scope, Designers, [Jz] ), CancellationToken.None );
                break;
            case Revocation.SignedOut:
                var signOut = await jz.SubmitFormAsync( HttpMethod.Delete, Routes.Session, signOutToken );
                Assert.Equal( HttpStatusCode.Redirect, signOut.Status );
                break;
            case Revocation.Deactivated:
                await database.WriteAsync( scope => UserOperations.Deactivate( scope, Jz ), CancellationToken.None );
                break;
            case Revocation.Banned:
                await database.WriteAsync( scope => UserBans.Ban( scope, Jz ), CancellationToken.None );
                break;
        }
    }

    /// <summary>
    /// What the client finds when it reconnects with the cookie it had: a member who lost the room is welcomed and
    /// keeps its user-scoped channels, but every room-scoped channel rejects it and no room broadcast reaches it; a
    /// signed-out, deactivated or banned user is turned away.
    /// </summary>
    private static async Task AssertReconnectAsync( SliceAppFactory factory, string cookie, PageSubscriptions subscriptions, Revocation revocation )
    {
        await using var again = await Realtime.ConnectWithCookieAsync( factory, cookie );
        if ( revocation != Revocation.MembershipRemoved )
        {
            Assert.Equal( Realtime.Unauthorized, await again.NextTextAsync( ) );
            Assert.Equal( Realtime.NormalClose, (await again.NextAsync( )).ToString( ) );
            return;
        }

        Assert.Equal( Realtime.Welcome, await again.NextTextAsync( ) );
        foreach ( var subscription in subscriptions.All )
        {
            var (reply, _) = await Realtime.SubscribeAsync( again, subscription.Identifier );
            var expectedReply = subscription.RoomScoped ? "reject_subscription" : "confirm_subscription";
            Assert.Equal( expectedReply, JsonNode.Parse( reply )!["type"]!.GetValue<string>( ) );
        }

        await Realtime.ExpectRejectedAsync( again, subscriptions.GuardedTurbo );

        new NumberedBroadcasts( factory.App.Cable, [.. subscriptions.Broadcastings] ).Round( );
        var frames = await again.CollectAsync( TimeSpan.FromMilliseconds( 300 ) );
        var receivers = frames.Select( frame => Realtime.Delivery( frame.ToString( ) ).Identifier ).Order( StringComparer.Ordinal );
        var expected = subscriptions.All
            .Where( subscription => subscription is { RoomScoped: false, Broadcasting: not null } )
            .Select( subscription => subscription.Identifier )
            .Order( StringComparer.Ordinal );
        Assert.Equal( expected, receivers );
    }
}
