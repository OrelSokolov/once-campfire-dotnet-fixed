using System.Globalization;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Policy;
using Campfire.Web.Security;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Campfire.Web.Tests.Views;

public sealed class PageCacheKeyTests
{
    [Theory]
    [InlineData( "route" )]
    [InlineData( "generation" )]
    [InlineData( "user" )]
    [InlineData( "session" )]
    [InlineData( "token" )]
    [InlineData( "target" )]
    [InlineData( "origin" )]
    [InlineData( "encoding" )]
    [InlineData( "format" )]
    [InlineData( "accept" )]
    [InlineData( "content-type" )]
    [InlineData( "xhr" )]
    [InlineData( "frame" )]
    [InlineData( "last-room" )]
    [InlineData( "agent" )]
    [InlineData( "locale" )]
    [InlineData( "culture" )]
    public async Task Changed_key_input_cannot_reuse_another_requests_body( string input )
    {
        using var services = new ServiceCollection( ).AddLogging( ).AddSingleton( new HttpOptions( ) ).AddSingleton<TimeProvider>( TimeProvider.System )
            .AddSingleton<RequestContextServices>( ).AddCampfireCompression( ).BuildServiceProvider( );
        var first = await Context( services );
        var second = await Context( services );
        var session = second.Current<CurrentSession>( )!;
        var httpContext = second.HttpContext;
        var route = "room";
        var generation = 1L;
        var previousCulture = CultureInfo.CurrentUICulture;
        var previousFormattingCulture = CultureInfo.CurrentCulture;
        try
        {
            var original = PageCacheRequest.Key( first, route, generation )!;
            (route, generation) = Mutate( second, input );
            var changed = PageCacheRequest.Key( second, route, generation )!;
            using var cache = new PageResponseCache( 1 << 20, TimeProvider.System );
            cache.AdvanceTo( 2 );
            var originalLeader = await cache.LookupAsync( "room", 2, original, false, TestContext.Current.CancellationToken );
            var changedLeader = await cache.LookupAsync( "room", 2, changed, false, TestContext.Current.CancellationToken );
            Assert.NotNull( originalLeader.Render );
            Assert.NotNull( changedLeader.Render );
            var originalWaiter = cache.LookupAsync( "room", 2, original, false, TestContext.Current.CancellationToken ).AsTask( );
            var changedWaiter = cache.LookupAsync( "room", 2, changed, false, TestContext.Current.CancellationToken ).AsTask( );
            var originalBody = new PageCacheEntry(
                SegmentedPageBody.Copy( ResponseBody.FromString( "first viewer" ) )!,
                [],
                DateTimeOffset.UtcNow.AddMinutes( 1 ) );
            var changedBody = new PageCacheEntry(
                SegmentedPageBody.Copy( ResponseBody.FromString( "other input" ) )!,
                [],
                DateTimeOffset.UtcNow.AddMinutes( 1 ) );
            PageResponseCache.Publish( originalLeader.Render, originalBody );
            Assert.Same( originalBody, (await originalWaiter).Entry );
            Assert.False( changedWaiter.IsCompleted );
            PageResponseCache.Publish( changedLeader.Render, changedBody );
            Assert.Same( changedBody, (await changedWaiter).Entry );
            originalLeader.Render!.Dispose( );
            changedLeader.Render!.Dispose( );
            Assert.Equal( 0, cache.Bytes );
            cache.Store(
                "room",
                2,
                original,
                new PageCacheEntry( SegmentedPageBody.Copy( ResponseBody.FromString( "first viewer" ) )!, [], DateTimeOffset.UtcNow.AddMinutes( 1 ) ) );
            Assert.NotNull( cache.Lookup( "room", 2, original, true, out _ ) );
            Assert.Null( cache.Lookup( "room", 2, changed, true, out _ ) );
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            CultureInfo.CurrentCulture = previousFormattingCulture;
        }
    }

    [Fact]
    public async Task Excessive_key_bypasses_and_tracing_does_not_split_entries( )
    {
        using var services = new ServiceCollection( ).AddLogging( ).AddSingleton( new HttpOptions( ) ).AddSingleton<TimeProvider>( TimeProvider.System )
            .AddSingleton<RequestContextServices>( ).AddCampfireCompression( ).BuildServiceProvider( );
        var context = await Context( services );
        var original = PageCacheRequest.Key( context, "room", 1 );
        context.HttpContext.Request.Headers["X-Request-Id"] = "trace-a";
        context.HttpContext.Request.Headers["traceparent"] = "trace-b";
        Assert.Equal( original, PageCacheRequest.Key( context, "room", 1 ) );
        context.HttpContext.Request.Headers.UserAgent = new string( 'x', PageResponseCache.MaximumKeyChars );
        Assert.Null( PageCacheRequest.Key( context, "room", 1 ) );
    }

    private static (string Route, long Generation) Mutate( RequestContext second, string input )
    {
        var session = second.Current<CurrentSession>( )!;
        var httpContext = second.HttpContext;
        var route = "room";
        var generation = 1L;
        switch ( input )
        {
            case "route":
                route = "messages";
                break;
            case "generation":
                generation = 2;
                break;
            case "user":
                second.SetCurrent( session with { User = new CurrentUser( 2 ) } );
                break;
            case "session":
                second.SetCurrent( session with { Id = 2 } );
                break;
            case "token":
                second.SetCurrent( session with { Token = "rotated" } );
                break;
            case "target":
                httpContext.Features.Get<IHttpRequestFeature>( )!.RawTarget = "/rooms/1?before=2";
                break;
            case "origin":
                httpContext.Request.Host = new HostString( "other.example", 8443 );
                break;
            case "encoding":
                httpContext.Request.Headers.AcceptEncoding = "gzip";
                break;
            case "format":
                second.Params["format"] = new InputText( "json" );
                break;
            case "accept":
                httpContext.Request.Headers.Accept = "application/json";
                break;
            case "content-type":
                httpContext.Request.ContentType = "application/json";
                break;
            case "xhr":
                httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
                break;
            case "frame":
                httpContext.Request.Headers["Turbo-Frame"] = "other-frame";
                break;
            case "last-room":
                httpContext.Request.Headers.Cookie = "last_room=2suffix";
                break;
            case "agent":
                httpContext.Request.Headers.UserAgent = "iPhone";
                break;
            case "culture":
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "fr-FR" );
                break;
            case "locale":
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo( "fr-FR" );
                break;
            default:
                throw new InvalidOperationException( input );
        }

        return (route, generation);
    }

    private static async Task<RequestContext> Context( IServiceProvider services )
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Method = "GET";
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString( "example.org" );
        httpContext.Request.Path = "/rooms/1";
        httpContext.Request.Headers.Accept = "text/html";
        httpContext.Features.Get<IHttpRequestFeature>( )!.RawTarget = "/rooms/1";
        httpContext.Features.Set( new AuthenticatedSession( new CurrentSession( 1, "token", new CurrentUser( 1 ) ) ) );
        return await RequestContext.CreateAsync( httpContext, TestContext.Current.CancellationToken );
    }
}
