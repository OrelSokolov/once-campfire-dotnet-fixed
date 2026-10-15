using Campfire.Web.App;
using Campfire.Web.Hosting;
using Campfire.Web.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

/// <summary>
/// The application builder <c>Program.cs</c> makes (<c>AddCampfireWeb</c>, then <c>AddCampfireApplication</c>), set up
/// for a test: the app's and the front server's storage in a fresh temporary directory, plain HTTP on ephemeral ports, no
/// log providers, and a ban list that bans nobody unless the test passes its own.
/// </summary>
internal sealed class ProductionAppBuilder
{
    private const string SecretKeyBaseVariable = "SECRET_KEY_BASE";

    private ProductionAppBuilder( WebApplicationBuilder builder, string storage )
    {
        Builder = builder;
        Storage = storage;
    }

    public WebApplicationBuilder Builder { get; }

    /// <summary>The temporary storage directory, for the app and the front server's certificates.</summary>
    public string Storage { get; }

    /// <summary>
    /// A builder whose front server is on when <paramref name="frontServer"/> is, with TLS for
    /// <paramref name="tlsDomain"/> when given (its certificate is read from <see cref="Storage"/>);
    /// <paramref name="settings"/> are applied over the test defaults.
    /// </summary>
    public static ProductionAppBuilder Create(
        bool frontServer,
        string? tlsDomain = null,
        IBanList? banList = null,
        params IEnumerable<KeyValuePair<string, string?>> settings )
    {
        var storage = Directory.CreateTempSubdirectory( "campfire-production-" ).FullName;
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Configuration.AddInMemoryCollection(
        [
            new( SecretKeyBaseVariable, "test-only" ),
            new( CampfireOptions.DisableSslVariable, "1" ),
            new( CampfireOptions.PortVariable, "0" ),
            new( CampfireOptions.StoragePathVariable, storage ),
            new( FrontServerOptions.EnabledVariable, frontServer ? "1" : "0" ),
            new( "THRUSTER_HTTP_PORT", "0" ),
            new( "THRUSTER_HTTPS_PORT", "0" ),
            new( "THRUSTER_TARGET_PORT", "0" ),
            new( "THRUSTER_TLS_DOMAIN", tlsDomain ?? "" ),
            new( "THRUSTER_STORAGE_PATH", storage ),
        ] );
        builder.Configuration.AddInMemoryCollection( settings );
        builder.Logging.ClearProviders( );
        builder.AddCampfireWeb( );
        builder.AddCampfireApplication( );
        builder.Services.AddSingleton<IBanList>( banList ?? new AllowAllTestBanList( ) );

        return new ProductionAppBuilder( builder, storage );
    }

    /// <summary>Builds the app, owned by a <see cref="FrontTestServer"/> that stops it and deletes the storage when disposed.</summary>
    public FrontTestServer Build( ) => new( Builder.Build( ), Storage );
}
