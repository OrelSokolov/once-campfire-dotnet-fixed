using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Tests.Hosting;

public sealed class FrontDeadlineTests
{
    [Fact]
    public void An_expired_request_is_aborted_once_even_while_its_handler_is_still_running( )
    {
        var httpContext = new DefaultHttpContext( );
        var lifetime = new Lifetime( );
        httpContext.Features.Set<IHttpRequestLifetimeFeature>( lifetime );
        httpContext.Request.ContentLength = 100;
        using var watchdog = new FrontRequestWatchdog( );
        using var deadline = new FrontRequestDeadline( httpContext, new FrontServerOptions
        {
            ReadTimeout = TimeSpan.FromMilliseconds( 1 ),
            WriteTimeout = TimeSpan.FromMilliseconds( 1 ),
        }, watchdog );

        var afterDeadline = Environment.TickCount64 + 100;
        for ( var tick = 0; tick < 20; tick++ )
        {
            deadline.Check( afterDeadline + tick * 50 );
        }

        Assert.Equal( 1, lifetime.Aborts );
    }

    private sealed class Lifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public int Aborts { get; private set; }
        public void Abort( ) => Aborts++;
    }
}
