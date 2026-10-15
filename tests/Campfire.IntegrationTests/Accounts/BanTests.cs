using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Hosting;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// Banning (<c>Users::BansController</c>, <c>User::Bannable</c>) in the operation's order: a ban per public session
/// address, sockets closed and sessions deleted, the user's messages removed later; a private session address rolls
/// the whole ban back with 422; a banned address can't write any more.
/// </summary>
public sealed class BanTests
{
    private const string PublicIp = "203.0.113.9";

    private static readonly long Jz = SliceAppFactory.Id( "jz" );

    [Fact]
    public async Task Banning_bans_the_session_addresses_signs_out_and_removes_the_messages( )
    {
        using var factory = new SliceAppFactory( );
        SessionCookies.InsertSession( factory, Jz, "jz-public", PublicIp );
        SessionCookies.InsertSession( factory, Jz, "jz-public-2", PublicIp );
        SessionCookies.InsertSession( factory, Jz, "jz-blank", "" );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.User( Jz ) );
        factory.Events.Clear( );

        var banned = await admin.SubmitFormAsync( HttpMethod.Post, Routes.UserBan( Jz ), token );
        await SliceWait.UntilAsync( ( ) => factory.Scalar<long>( $"SELECT COUNT(*) FROM messages WHERE creator_id = {Jz}" ) == 0 );
        var page = await admin.GetAsync( Routes.User( Jz ) );

        Assert.Equal( HttpStatusCode.Found, banned.Status );
        Assert.Equal( $"{factory.BaseAddress}users/{Jz}", banned.Location );
        Assert.Equal( PublicIp, factory.Scalar<string>( $"SELECT group_concat(ip_address) FROM bans WHERE user_id = {Jz}" ) );
        Assert.Equal( 2L, factory.Scalar<long>( $"SELECT status FROM users WHERE id = {Jz}" ) );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT COUNT(*) FROM sessions WHERE user_id = {Jz}" ) );
        Assert.Equal(
            [$"disconnect user {Jz} reconnect=false", $"banned_content_job user {Jz}"],
            factory.Events.Only( "disconnect", "banned_content_job" ) );
        Assert.Contains( "class=\"flex flex-column gap banned\"", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_private_session_address_rolls_the_whole_ban_back_with_422( )
    {
        using var factory = new SliceAppFactory( );
        SessionCookies.InsertSession( factory, Jz, "jz-public", PublicIp );
        SessionCookies.InsertSession( factory, Jz, "jz-private", "10.0.0.7" );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.User( Jz ) );
        factory.Events.Clear( );

        var refused = await admin.SubmitFormAsync( HttpMethod.Post, Routes.UserBan( Jz ), token );

        Assert.Equal( HttpStatusCode.UnprocessableEntity, refused.Status );
        Assert.Contains( "<title>422 Unprocessable Entity</title>", refused.Body, StringComparison.Ordinal );
        Assert.Contains( "<h1>Unprocessable Entity</h1>", refused.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "Exception", refused.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "password_digest", refused.Body, StringComparison.Ordinal );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM bans" ) );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT status FROM users WHERE id = {Jz}" ) );
        Assert.Equal( 2L, factory.Scalar<long>( $"SELECT COUNT(*) FROM sessions WHERE user_id = {Jz}" ) );
        Assert.Empty( factory.Events.Only( "banned_content_job" ) );
    }

    [Fact]
    public async Task A_banned_address_cannot_write_and_unbanning_lifts_it( )
    {
        using var factory = new TrustedProxyApp( );
        SessionCookies.InsertSession( factory, Jz, "jz-public", PublicIp );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.User( Jz ) );
        await admin.SubmitFormAsync( HttpMethod.Post, Routes.UserBan( Jz ), token );

        using var visitor = factory.Client( );
        var visitorToken = await visitor.CsrfTokenAsync( );
        var blocked = await SignInFromAsync( visitor, visitorToken, PublicIp );
        var unbanned = await admin.SubmitFormAsync( HttpMethod.Delete, Routes.UserBan( Jz ), token );
        var allowed = await SignInFromAsync( visitor, visitorToken, PublicIp );

        Assert.Equal( HttpStatusCode.TooManyRequests, blocked.Status );
        Assert.Equal( HttpStatusCode.Found, unbanned.Status );
        Assert.Equal( $"{factory.BaseAddress}users/{Jz}", unbanned.Location );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM bans" ) );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT status FROM users WHERE id = {Jz}" ) );
        Assert.Equal( HttpStatusCode.Unauthorized, allowed.Status );
    }

    [Fact]
    public async Task Members_cannot_ban( )
    {
        using var factory = new SliceAppFactory( );
        SessionCookies.InsertSession( factory, Jz, "jz-public", PublicIp );
        using var member = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await member.CsrfTokenAsync( Routes.User( Jz ) );

        var refused = await member.SubmitFormAsync( HttpMethod.Post, Routes.UserBan( Jz ), token );

        Assert.Equal( HttpStatusCode.Forbidden, refused.Status );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM bans" ) );
    }

    /// <summary>
    /// A sign-in attempt with wrong credentials through a proxy that reports <paramref name="ipAddress"/> as the client
    /// (loopback is trusted).
    /// </summary>
    private static async Task<SliceReply> SignInFromAsync( SliceClient client, string token, string ipAddress )
    {
        using var request = new HttpRequestMessage( HttpMethod.Post, Routes.Session )
        {
            Content = new FormUrlEncodedContent( [new( "email_address", "nobody@example.com" ), new( "password", "x" )] ),
        };
        request.Headers.Add( BrowserHeaders.CsrfToken, token );
        request.Headers.Add( "X-Forwarded-For", ipAddress );
        return await client.SendAsync( request );
    }

    private sealed class TrustedProxyApp : SliceAppFactory
    {
        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.UseSetting( "CAMPFIRE_TRUSTED_PROXIES", "127.0.0.1" );
        }
    }
}
