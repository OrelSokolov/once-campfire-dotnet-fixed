using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Security;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Campfire.Web.Tests.Views;

public sealed class PageCacheAdmissionTests
{
    [Theory]
    [InlineData( "valid", true )]
    [InlineData( "head", false )]
    [InlineData( "post", false )]
    [InlineData( "bot", false )]
    [InlineData( "flash", false )]
    [InlineData( "session", false )]
    [InlineData( "status", false )]
    [InlineData( "no-store", false )]
    [InlineData( "oversize", false )]
    [InlineData( "superseded", false )]
    public async Task Only_admitted_responses_can_be_reused( string change, bool admitted )
    {
        using var services = new ServiceCollection( ).AddLogging( ).AddSingleton( new HttpOptions( ) )
            .AddSingleton<TimeProvider>( TimeProvider.System ).AddSingleton<RequestContextServices>( ).BuildServiceProvider( );
        var session = new CurrentSession( 1, "token", new CurrentUser( 1 ) );
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Method = "GET";
        httpContext.Features.Set( new AuthenticatedSession( session ) );
        var context = await RequestContext.CreateAsync( httpContext, TestContext.Current.CancellationToken );
        using var cache = new PageResponseCache( 64 << 20, TimeProvider.System );
        cache.AdvanceTo( 1 );
        var request = new PageCacheRequest( cache, context, "messages", 1, "key", session );
        var result = FrontendResult.WithBody( 200, "text/html", new BytesBody( Encoding.UTF8.GetBytes( "admitted body" ) ) );
        switch ( change )
        {
            case "head":
                httpContext.Request.Method = "HEAD";
                break;
            case "post":
                httpContext.Request.Method = "POST";
                break;
            case "bot":
                context.SetCurrent( new AuthenticationMethod( AuthenticatedBy.BotKey ) );
                break;
            case "flash":
                context.Flash.Alert = "request-local alert";
                break;
            case "session":
                context.SetCurrent( session with { Token = "rotated" } );
                break;
            case "status":
                result.Status = 422;
                break;
            case "no-store":
                result.Headers.CacheControl = "no-store";
                break;
            case "oversize":
                result.Body = new BytesBody( new byte[PageResponseCache.MaximumBodyBytes + 1] );
                break;
            case "superseded":
                cache.AdvanceTo( 2 );
                break;
        }

        request.Store( result );
        var stored = cache.Lookup( "messages", cache.NewestGeneration, "key", true, out _ );
        Assert.Equal( admitted, stored is not null );
    }
}
