namespace Campfire.Web.Http;

/// <summary>Ordinary browser preferences. Authentication and flash use separate protected facilities.</summary>
public sealed class RequestCookies( HttpContext httpContext )
{
    public string? this[string name] => httpContext.Request.Cookies[name];
    public void SetPreference( string name, string value ) => httpContext.Response.Cookies.Append( name, value, new CookieOptions
    {
        HttpOnly = true,
        Secure = httpContext.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromDays( 365 )
    } );
}
