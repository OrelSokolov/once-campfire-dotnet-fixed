using System.Globalization;
using Campfire.Web.Hosting;
using Campfire.Web.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Campfire.Web.Tests.Http;

/// <summary>Builds a <see cref="RequestContext"/> over a <see cref="DefaultHttpContext"/> for unit tests.</summary>
internal static class TestContexts
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse( "2024-06-01T12:00:00Z", CultureInfo.InvariantCulture );

    public static async Task<RequestContext> CreateAsync(
        string method = "GET",
        string path = "/",
        Action<HttpRequest>? configure = null,
        HttpOptions? options = null )
    {
        var services = new ServiceCollection( );
        services.AddSingleton( options ?? new HttpOptions( ) );
        services.AddSingleton<IDataProtectionProvider>( new EphemeralDataProtectionProvider( ) );
        services.AddLogging( );
        services.AddSingleton<TimeProvider>( new FrozenTimeProvider( Now ) );
        services.AddSingleton<ILoggerFactory>( NullLoggerFactory.Instance );
        services.AddSingleton<RequestContextServices>( );

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider( ) };
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        httpContext.Request.Headers.Host = "chat.example.com";
        configure?.Invoke( httpContext.Request );

        return await RequestContext.CreateAsync( httpContext, CancellationToken.None );
    }
}
