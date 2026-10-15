using System.Globalization;
using System.Security.Claims;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Campfire.IntegrationTests.Support;

/// <summary>Fixture sessions use the production ticket format; only their database setup bypasses the sign-in form.</summary>
public static class SessionCookies
{
    /// <summary>
    /// Signs <paramref name="client"/> in as <paramref name="userId"/> without the form: a session row and its ticket
    /// cookie, which expires at <paramref name="expiresAt"/> (by default after the cookie's lifetime).
    /// </summary>
    public static void SignInAs( this SliceClient client, SliceAppFactory factory, long userId, DateTimeOffset? expiresAt = null )
    {
        ArgumentNullException.ThrowIfNull( client );
        ArgumentNullException.ThrowIfNull( factory );

        var token = Guid.NewGuid( ).ToString( "N" );
        InsertSession( factory, userId, token, "127.0.0.1" );
        var sessionId = factory.Scalar<long>( $"SELECT id FROM sessions WHERE token = '{token}'" );
        Claim[] claims =
        [
            new Claim( ClaimTypes.NameIdentifier, sessionId.ToString( CultureInfo.InvariantCulture ) ),
            new Claim( BrowserAuthentication.SessionTokenClaim, token ),
        ];
        var principal = new ClaimsPrincipal( new ClaimsIdentity( claims, BrowserAuthentication.Scheme ) );
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = expiresAt ?? DateTimeOffset.UtcNow.AddDays( BrowserAuthentication.LifetimeDays )
        };
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>( ).Get( BrowserAuthentication.Scheme );
        var ticket = new AuthenticationTicket( principal, properties, BrowserAuthentication.Scheme );
        client.AddCookie( BrowserAuthentication.CookieName, options.TicketDataFormat.Protect( ticket ) );
    }

    /// <summary>A session row for <paramref name="userId"/>, signed in from <paramref name="ipAddress"/> at the fixture's time.</summary>
    public static void InsertSession( SliceAppFactory factory, long userId, string token, string ipAddress )
    {
        ArgumentNullException.ThrowIfNull( factory );

        factory.Execute(
            "INSERT INTO sessions (user_id, token, ip_address, user_agent, last_active_at, created_at, updated_at) " +
            $"VALUES ({userId}, '{token}', '{ipAddress}', 'Mozilla/5.0', '2026-09-26 13:14:52', '2026-09-26 13:14:52', '2026-09-26 13:14:52')" );
    }
}
