using System.Globalization;
using System.Security.Claims;
using Campfire.Web.Security;
using Campfire.Web.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Tests.Security;

public sealed class PageTicketMemoTests
{
    [Fact]
    public void Exact_reassembled_cookies_reuse_only_plain_credentials( )
    {
        using var fixture = new Tickets( );
        using var memo = fixture.Memo( );
        var cookie = fixture.Cookie( 11, "first-token" );
        Assert.Equal( 11, memo.Read( fixture.Http( cookie ), fixture.Options )!.Value.SessionId );
        Assert.Equal( 1, fixture.Format.Reads );
        var half = cookie.Length / 2;
        var chunked = new DefaultHttpContext( );
        chunked.Request.Headers.Cookie = $"{BrowserAuthentication.CookieName}=chunks-2; {BrowserAuthentication.CookieName}C1={cookie[..half]}; " +
            $"{BrowserAuthentication.CookieName}C2={cookie[half..]}";
        Assert.Equal( "first-token", memo.Read( chunked, fixture.Options )!.Value.Token );
        Assert.Equal( 1, fixture.Format.Reads );
        var other = fixture.Cookie( 12, "other-token" );
        Assert.Equal( 12, memo.Read( fixture.Http( other ), fixture.Options )!.Value.SessionId );
        Assert.Equal( "first-token", memo.Read( fixture.Http( cookie ), fixture.Options )!.Value.Token );
        Assert.Equal( 2, fixture.Format.Reads );
    }

    [Fact]
    public void A_memo_hit_still_checks_ticket_expiry( )
    {
        using var fixture = new Tickets( );
        using var memo = fixture.Memo( );
        var cookie = fixture.Cookie( 11, "token", fixture.Clock.Now.AddMinutes( 1 ) );
        Assert.NotNull( memo.Read( fixture.Http( cookie ), fixture.Options ) );
        Assert.NotNull( memo.Read( fixture.Http( cookie ), fixture.Options ) );
        Assert.Equal( 1, fixture.Format.Reads );
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes( 2 );
        Assert.Null( memo.Read( fixture.Http( cookie ), fixture.Options ) );
        Assert.Equal( 0, memo.Bytes );
    }

    [Fact]
    public async Task Key_creation_clears_the_memo_and_revocation_cannot_reuse_a_ticket( )
    {
        using var fixture = new Tickets( );
        using var memo = fixture.Memo( );
        var cookie = fixture.Cookie( 11, "token" );
        Assert.NotNull( memo.Read( fixture.Http( cookie ), fixture.Options ) );
        var reads = fixture.Format.Reads;
        fixture.Keys.CreateNewKey( DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays( 90 ) );
        Assert.NotNull( memo.Read( fixture.Http( cookie ), fixture.Options ) );
        Assert.Equal( reads + 1, fixture.Format.Reads );
        fixture.Keys.RevokeAllKeys( DateTimeOffset.UtcNow.AddMinutes( 1 ), "test key revocation" );
        // The framework refreshes its ring asynchronously. Establish that its ordinary decryptor
        // sees the revocation before checking that a memo hit cannot extend the old ring's lifetime.
        await Polling.WaitUntilAsync( ( ) => fixture.Format.Unprotect( cookie ) is null, "the key ring never saw the revocation" );

        Assert.Null( fixture.Format.Unprotect( cookie ) );
        Assert.Null( memo.Read( fixture.Http( cookie ), fixture.Options ) );
        Assert.Equal( 0, memo.Bytes );
    }

    [Fact]
    public void Invalid_or_tampered_cookies_never_enter_the_memo( )
    {
        using var fixture = new Tickets( );
        using var memo = fixture.Memo( );
        var cookie = fixture.Cookie( 11, "token" );
        var bytes = WebEncoders.Base64UrlDecode( cookie );
        bytes[^1] ^= 1;
        foreach ( var value in new[] { "not-a-ticket", WebEncoders.Base64UrlEncode( bytes ) } )
        {
            var before = fixture.Format.Reads;
            Assert.Null( memo.Read( fixture.Http( value ), fixture.Options ) );
            Assert.Null( memo.Read( fixture.Http( value ), fixture.Options ) );
            Assert.Equal( before + 2, fixture.Format.Reads );
            Assert.Equal( 0, memo.Bytes );
        }
    }

    [Fact]
    public async Task A_decrypt_started_before_key_revocation_cannot_repopulate_the_memo( )
    {
        using var fixture = new Tickets( );
        using var memo = fixture.Memo( );
        var cookie = fixture.Cookie( 11, "token" );
        using var entered = new ManualResetEventSlim( );
        using var release = new ManualResetEventSlim( );
        var first = 0;
        fixture.Format.AfterRead = _ =>
        {
            if ( Interlocked.Exchange( ref first, 1 ) == 0 )
            {
                entered.Set( );
                Assert.True( release.Wait( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) );
            }
        };
        var delayed = Task.Run( ( ) => memo.Read( fixture.Http( cookie ), fixture.Options ), TestContext.Current.CancellationToken );
        try
        {
            Assert.True( entered.Wait( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) );
            fixture.Keys.RevokeAllKeys( DateTimeOffset.UtcNow.AddMinutes( 1 ), "concurrent key revocation" );
            await Polling.WaitUntilAsync( ( ) => fixture.Format.Unprotect( cookie ) is null, "the key ring never saw the revocation" );

            Assert.Null( fixture.Format.Unprotect( cookie ) );
            Assert.Null( memo.Read( fixture.Http( cookie ), fixture.Options ) );
        }
        finally
        {
            release.Set( );
        }

        // The already-started request may finish, but its old decrypt cannot become a future hit.
        await delayed;
        Assert.Equal( 0, memo.Bytes );
        Assert.Null( memo.Read( fixture.Http( cookie ), fixture.Options ) );
    }

    [Fact]
    public void Bounds_and_recent_use_determine_eviction( )
    {
        using var fixture = new Tickets( );
        var cookieA = fixture.Cookie( 11, "token-a" );
        var cookieB = fixture.Cookie( 12, "token-b" );
        var cookieC = fixture.Cookie( 13, "token-c" );
        using var measured = fixture.Memo( );
        Assert.NotNull( measured.Read( fixture.Http( cookieA ), fixture.Options ) );
        var budget = 2 * measured.Bytes;
        using var memo = fixture.Memo( budget );
        Assert.NotNull( memo.Read( fixture.Http( cookieA ), fixture.Options ) );
        Assert.NotNull( memo.Read( fixture.Http( cookieB ), fixture.Options ) );
        Assert.NotNull( memo.Read( fixture.Http( cookieA ), fixture.Options ) );
        Assert.NotNull( memo.Read( fixture.Http( cookieC ), fixture.Options ) );
        var reads = fixture.Format.Reads;
        Assert.NotNull( memo.Read( fixture.Http( cookieA ), fixture.Options ) );
        Assert.Equal( reads, fixture.Format.Reads );
        Assert.NotNull( memo.Read( fixture.Http( cookieB ), fixture.Options ) );
        Assert.Equal( reads + 1, fixture.Format.Reads );
        Assert.InRange( memo.Bytes, 0, budget );
    }

    private sealed class Tickets : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory( "campfire-ticket-test-" );
        private readonly ServiceProvider _services;

        internal Tickets( )
        {
            _services = new ServiceCollection( ).AddLogging( ).AddBrowserAuthentication( _directory.FullName ).BuildServiceProvider( );
            Options = _services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>( ).Get( BrowserAuthentication.Scheme );
            Format = new CountingFormat( Options.TicketDataFormat );
            Options.TicketDataFormat = Format;
            Options.TimeProvider = Clock;
        }

        internal Clock Clock { get; } = new( );

        internal CookieAuthenticationOptions Options { get; }

        internal CountingFormat Format { get; }

        internal IKeyManager Keys => _services.GetRequiredService<IKeyManager>( );

        internal PageTicketMemo Memo( long budget = PageTicketMemo.DefaultBudget ) =>
            new( Keys, _services.GetRequiredService<IKeyRingProvider>( ), budget: budget );

        internal string Cookie( long id, string token, DateTimeOffset? expires = null )
        {
            Claim[] claims =
            [
                new Claim( ClaimTypes.NameIdentifier, id.ToString( CultureInfo.InvariantCulture ) ),
                new Claim( BrowserAuthentication.SessionTokenClaim, token ),
            ];
            var principal = new ClaimsPrincipal( new ClaimsIdentity( claims, BrowserAuthentication.Scheme ) );
            var properties = new AuthenticationProperties { ExpiresUtc = expires ?? Clock.Now.AddDays( 1 ) };

            return Format.Protect( new AuthenticationTicket( principal, properties, BrowserAuthentication.Scheme ) );
        }

        internal DefaultHttpContext Http( string cookie )
        {
            var httpContext = new DefaultHttpContext( );
            httpContext.Request.Headers.Cookie = Options.Cookie.Name + "=" + cookie;
            return httpContext;
        }

        public void Dispose( )
        {
            _services.Dispose( );
            _directory.Delete( true );
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow( ) => Now;
    }

    private sealed class CountingFormat( ISecureDataFormat<AuthenticationTicket> inner ) : ISecureDataFormat<AuthenticationTicket>
    {
        private int _reads;

        internal int Reads => Volatile.Read( ref _reads );

        internal Action<AuthenticationTicket?>? AfterRead { get; set; }

        public string Protect( AuthenticationTicket data ) => inner.Protect( data );

        public string Protect( AuthenticationTicket data, string? purpose ) => inner.Protect( data, purpose );

        public AuthenticationTicket? Unprotect( string? text )
        {
            Interlocked.Increment( ref _reads );
            var result = inner.Unprotect( text );
            AfterRead?.Invoke( result );
            return result;
        }

        public AuthenticationTicket? Unprotect( string? text, string? purpose )
        {
            Interlocked.Increment( ref _reads );
            var result = inner.Unprotect( text, purpose );
            AfterRead?.Invoke( result );
            return result;
        }
    }
}
