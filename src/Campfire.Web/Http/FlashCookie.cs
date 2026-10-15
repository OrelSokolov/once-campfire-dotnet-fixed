using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace Campfire.Web.Http;

/// <summary>One short-lived protected flash cookie, independent of the authentication ticket.</summary>
public static class FlashCookie
{
    public const string Name = ".Campfire.Flash";
    public const string Purpose = "Campfire.Flash.v1";
    public const int MaximumLength = 2048;

    public static Flash Read( HttpContext httpContext )
    {
        var value = httpContext.Request.Cookies[Name];
        if ( string.IsNullOrEmpty( value ) || value.Length > MaximumLength )
        {
            return new Flash( );
        }

        httpContext.Response.Cookies.Delete( Name, Options( httpContext ) );
        var protector = httpContext.RequestServices.GetRequiredService<IDataProtectionProvider>( ).CreateProtector( Purpose );
        try
        {
            return Flash.FromSessionValue( JsonNode.Parse( protector.Unprotect( value ) ) );
        }
        catch ( Exception error ) when ( error is CryptographicException or JsonException or InvalidOperationException )
        {
            // A stale or tampered flash is untrusted display data, never a credential.
            return new Flash( );
        }
    }

    public static void Write( HttpContext httpContext, Flash flash )
    {
        var node = flash.ToSessionValue( );
        if ( node is null )
        {
            return;
        }

        var protector = httpContext.RequestServices.GetRequiredService<IDataProtectionProvider>( ).CreateProtector( Purpose );
        var value = protector.Protect( node.ToJsonString( ) );
        if ( value.Length > MaximumLength )
        {
            throw new InvalidOperationException( "Flash message exceeds the cookie limit." );
        }

        httpContext.Response.Cookies.Append( Name, value, Options( httpContext ) );
    }

    private static CookieOptions Options( HttpContext httpContext ) => new( )
    {
        HttpOnly = true,
        Secure = httpContext.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromMinutes( 5 )
    };
}
