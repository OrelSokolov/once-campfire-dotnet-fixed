using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Campfire.Application.Users;
using Campfire.Web.Policy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Security;

/// <summary>Browser cookies cannot authorize bot endpoints; their independent key is the only credential.</summary>
public sealed class BotKeyAuthentication( IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder )
    : AuthenticationHandler<AuthenticationSchemeOptions>( options, logger, encoder )
{
    public new const string Scheme = "Campfire.BotKey";
    public const string Policy = "BotKey";
    public const string KeyParameter = "bot_key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync( )
    {
        var key = Request.RouteValues[KeyParameter]?.ToString( ) ?? Request.Query[KeyParameter].ToString( );
        if ( string.IsNullOrWhiteSpace( key ) )
        {
            return AuthenticateResult.NoResult( );
        }

        var store = Context.RequestServices.GetRequiredService<IAuthenticationStore>( );
        var user = await store.AuthenticateBotAsync( key.Trim( ), Context.RequestAborted );
        if ( user?.Record is not User { IsBot: true, IsActive: true } )
        {
            return AuthenticateResult.Fail( "Invalid bot key." );
        }

        Context.Features.Set( new AuthenticatedBot( user ) );
        var identity = new ClaimsIdentity( [new Claim( ClaimTypes.NameIdentifier, user.Id.ToString( CultureInfo.InvariantCulture ) )], Scheme );
        return AuthenticateResult.Success( new AuthenticationTicket( new ClaimsPrincipal( identity ), Scheme ) );
    }

    protected override Task HandleChallengeAsync( AuthenticationProperties properties ) =>
        ErrorResults.WriteAsync( Context, StatusCodes.Status401Unauthorized );
}
