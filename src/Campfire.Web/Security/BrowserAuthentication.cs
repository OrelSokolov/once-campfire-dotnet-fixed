using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Campfire.Application.Data;
using Campfire.Application.Security;
using Campfire.Application.Sessions;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace Campfire.Web.Security;

/// <summary>The ticket identifies a session row and its random token; current authority is read from SQLite for each request.</summary>
public static class BrowserAuthentication
{
    public const string Scheme = "Campfire.Browser";
    public const string DefaultScheme = "Campfire.Credential";
    public const string SessionTokenClaim = "Campfire.SessionToken";
    public const string CookieName = ".Campfire.Auth";
    public const string SignInPath = "/session/new";
    public const string ReturnUrlParameter = "returnUrl";
    public const int LifetimeDays = 30;

    public static IServiceCollection AddBrowserAuthentication( this IServiceCollection services, string storageRoot )
    {
        services.AddDataProtection( ).SetApplicationName( "Campfire" )
            .PersistKeysToFileSystem( new DirectoryInfo( Path.Combine( storageRoot, "keys" ) ) );
        services.AddAuthentication( options =>
        {
            options.DefaultAuthenticateScheme = DefaultScheme;
            options.DefaultSignInScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
        } )
            .AddCookie( Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays( LifetimeDays );
                options.SlidingExpiration = false;
                // A frozen benchmark/business clock must not freeze ticket expiry.
                options.TimeProvider = TimeProvider.System;
                options.LoginPath = SignInPath;
                options.ReturnUrlParameter = ReturnUrlParameter;
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = ValidatePrincipalAsync,
                    OnRedirectToLogin = ChallengeAsync,
                    OnRedirectToAccessDenied = context => ErrorResults.WriteAsync( context.HttpContext, StatusCodes.Status403Forbidden )
                };
            } )
            .AddScheme<AuthenticationSchemeOptions, BotKeyAuthentication>( BotKeyAuthentication.Scheme, _ => { } )
            .AddPolicyScheme( DefaultScheme, DefaultScheme, options => options.ForwardDefaultSelector = SchemeFor );
        services.AddAuthorization( options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder( Scheme )
                .RequireAuthenticatedUser( ).Build( );
            options.FallbackPolicy = options.DefaultPolicy;
            options.AddPolicy( BotKeyAuthentication.Policy, policy => policy
                .AddAuthenticationSchemes( BotKeyAuthentication.Scheme ).RequireAuthenticatedUser( ) );
        } );

        return services;
    }

    public static async Task SignInAsync( HttpContext httpContext, CurrentSession session )
    {
        Claim[] claims =
        [
            new Claim( ClaimTypes.NameIdentifier, session.Id.ToString( CultureInfo.InvariantCulture ) ),
            new Claim( SessionTokenClaim, session.Token ),
        ];
        var identity = new ClaimsIdentity( claims, Scheme );
        httpContext.Features.Set( new AuthenticatedSession( session ) );
        httpContext.User = new ClaimsPrincipal( identity );
        await httpContext.SignInAsync( Scheme, httpContext.User, new AuthenticationProperties { IsPersistent = true } );
        AppendRailsSessionTokenCookie( httpContext, session.Token );
    }

    /// <summary>
    /// Rails names its authentication cookie <c>session_token</c> (the session's token, signed with the cookie
    /// verifier over <c>SECRET_KEY_BASE</c>); clients written against that contract key on the name. Mirror the
    /// session token under it, signed the same way, so the native <c>.Campfire.Auth</c> ticket stays authoritative.
    /// Without <c>SECRET_KEY_BASE</c> (a fresh installation) the alias is skipped.
    /// </summary>
    private static void AppendRailsSessionTokenCookie( HttpContext httpContext, string token )
    {
        var secret = Environment.GetEnvironmentVariable( RailsSignedIds.SecretVariable );
        if ( string.IsNullOrEmpty( secret ) )
        {
            return;
        }

        var key = Rfc2898DeriveBytes.Pbkdf2( Encoding.UTF8.GetBytes( secret ), Encoding.UTF8.GetBytes( "signed cookie" ), 1000, HashAlgorithmName.SHA1, 64 );
        var digest = Convert.ToHexString( HMACSHA256.HashData( key, Encoding.UTF8.GetBytes( token ) ) ).ToLower( CultureInfo.InvariantCulture );
        var flags = httpContext.Request.IsHttps ? "; secure" : "";
        httpContext.Response.Headers.Append( "Set-Cookie",
            $"session_token={Uri.EscapeDataString( token )}--{digest}; path=/; httponly; samesite=lax{flags}; max-age={LifetimeDays * 24 * 60 * 60}" );
    }

    /// <summary>Expires the Rails-named alias alongside the native ticket (<c>terminate_current_session_for</c>).</summary>
    public static void DeleteRailsSessionTokenCookie( HttpContext httpContext ) =>
        httpContext.Response.Headers.Append( "Set-Cookie", "session_token=; path=/; max-age=0" );

    public static async Task<CurrentSession?> ReadSessionAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var sessionClaim = httpContext.User.FindFirstValue( ClaimTypes.NameIdentifier );
        if ( !long.TryParse( sessionClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId ) )
        {
            return null;
        }

        var database = httpContext.RequestServices.GetRequiredService<CampfireDatabase>( );
        var session = await database.ReadAsync( db => FindSessionById( db, sessionId ), cancellationToken );
        return session is not null && MatchesToken( httpContext.User.FindFirstValue( SessionTokenClaim ), session.Token ) ? session : null;
    }

    internal static CurrentSession? FindSessionById( CampfireDb db, long sessionId )
    {
        var authority = SessionAuthority.FindById( db, sessionId );
        if ( authority is null )
        {
            return null;
        }

        var user = authority.User;
        if ( !user.IsActive || user.IsBot )
        {
            return null;
        }

        var session = authority.Session;
        return new CurrentSession( session.Id, session.Token, new CurrentUser( user.Id, user ), session );
    }

    internal static bool MatchesToken( string? ticketToken, string storedToken ) => ticketToken is { Length: <= 128 } &&
        CryptographicOperations.FixedTimeEquals( Encoding.UTF8.GetBytes( ticketToken ), Encoding.UTF8.GetBytes( storedToken ) );

    private static async Task ValidatePrincipalAsync( CookieValidatePrincipalContext context )
    {
        // ReadSessionAsync deliberately reads the principal being validated, not a stale HttpContext.User.
        var previous = context.HttpContext.User;
        context.HttpContext.User = context.Principal!;
        CurrentSession? session;
        try
        {
            session = await ReadSessionAsync( context.HttpContext, context.HttpContext.RequestAborted );
        }
        finally
        {
            context.HttpContext.User = previous;
        }

        if ( session is null )
        {
            context.RejectPrincipal( );
            await context.HttpContext.SignOutAsync( Scheme );
            return;
        }

        context.HttpContext.Features.Set( new AuthenticatedSession( session ) );
    }

    /// <summary>Endpoints behind the bot-key policy authenticate with the key; everything else with the browser cookie.</summary>
    private static string SchemeFor( HttpContext httpContext )
    {
        var policies = httpContext.GetEndpoint( )?.Metadata.GetOrderedMetadata<IAuthorizeData>( );

        return policies?.Any( metadata => metadata.Policy == BotKeyAuthentication.Policy ) == true ? BotKeyAuthentication.Scheme : Scheme;
    }

    private static Task ChallengeAsync( RedirectContext<CookieAuthenticationOptions> context )
    {
        var accept = context.Request.Headers.Accept.ToString( );
        if ( accept.Contains( ContentTypes.Json, StringComparison.OrdinalIgnoreCase )
            || accept.Contains( ContentTypes.TurboStream, StringComparison.OrdinalIgnoreCase ) )
        {
            return ErrorResults.WriteAsync( context.HttpContext, StatusCodes.Status401Unauthorized );
        }

        context.Response.Redirect( context.RedirectUri );
        return Task.CompletedTask;
    }
}
