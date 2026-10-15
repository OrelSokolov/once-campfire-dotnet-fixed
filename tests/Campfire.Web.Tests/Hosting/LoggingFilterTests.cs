using Campfire.Web.App;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

public sealed class LoggingFilterTests
{
    [Fact]
    public void Framework_info_is_filtered_but_warnings_and_application_info_are_delivered( )
    {
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Configuration.AddInMemoryCollection( [new( "SECRET_KEY_BASE", "test-only" ), new( "RAILS_LOG_LEVEL", "info" )] );
        using var logs = new LogCapture( );
        builder.Logging.ClearProviders( );
        builder.Logging.AddProvider( logs );
        builder.AddCampfireWeb( );
        builder.AddCampfireApplication( );
        using var services = builder.Services.BuildServiceProvider( );
        var factory = services.GetRequiredService<ILoggerFactory>( );
        var framework = factory.CreateLogger( "Microsoft.AspNetCore.Hosting.Diagnostics" );
        var application = factory.CreateLogger( "Campfire.Application" );

        framework.LogInformation( "request info" );
        framework.LogWarning( "request warning" );
        application.LogInformation( "application info" );

        Assert.Equal( ["request warning", "application info"], logs.Messages );
    }
}
