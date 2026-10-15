using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Campfire.Web.Security;

/// <summary>Read the browser ticket for admission without installing a request-lifetime authentication handler.</summary>
internal static class CableCookie
{
    public static CableCredential? Read( HttpContext httpContext, CookieAuthenticationOptions options )
    {
        var cookie = options.CookieManager.GetRequestCookie( httpContext, options.Cookie.Name! );
        return Read( cookie, options );
    }

    internal static CableCredential? Read( string? cookie, CookieAuthenticationOptions options )
    {
        if ( string.IsNullOrEmpty( cookie ) )
        {
            return null;
        }

        // Campfire uses the protected ticket directly (no external ticket store or TLS token binding).
        // Chunk reassembly, cryptographic purpose and ticket lifetime are the browser scheme's own settings.
        var ticket = options.TicketDataFormat.Unprotect( cookie );
        if ( ticket is null || IsExpired( ticket, options ) )
        {
            return null;
        }

        var principal = ticket.Principal;
        if ( !long.TryParse( principal.FindFirstValue( ClaimTypes.NameIdentifier ), NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId )
            || principal.FindFirstValue( BrowserAuthentication.SessionTokenClaim ) is not { Length: <= 128 } token )
        {
            return null;
        }

        // Only these plain values escape; the principal, ticket and properties die before the database await.
        return new CableCredential( sessionId, token, ticket.Properties.ExpiresUtc );
    }

    private static bool IsExpired( AuthenticationTicket ticket, CookieAuthenticationOptions options ) =>
        ticket.Properties.ExpiresUtc is { } expires && expires < (options.TimeProvider ?? TimeProvider.System).GetUtcNow( );
}
