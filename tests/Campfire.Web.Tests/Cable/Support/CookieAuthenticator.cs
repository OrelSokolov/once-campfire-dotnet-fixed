using Campfire.Application.Security;
using Campfire.Web.Cable;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary><c>Cookie: session_token=&lt;id&gt;</c>: users 1 and 2 exist and are members of room 1 only.</summary>
public sealed class CookieAuthenticator : ICableConnectionAuthenticator
{
    public ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var cookie = httpContext.Request.Headers.Cookie.ToString( );
        if ( !cookie.StartsWith( "session_token=", StringComparison.Ordinal )
            || !long.TryParse( cookie["session_token=".Length..], out var id )
            || id is not (1 or 2) )
        {
            return ValueTask.FromResult<CableIdentity?>( null );
        }

        var user = new TestUser( id, $"User {id}", [1] );

        return ValueTask.FromResult<CableIdentity?>( CableIdentity.ForRecord( GlobalId.For( "User", id ), user ) );
    }
}
