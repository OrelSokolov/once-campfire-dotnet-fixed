using System.Globalization;
using Campfire.Application.Data;
using Campfire.Application.Tests.Fixtures;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Cable;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// The whole app, as Program composes it, on real Kestrel at a loopback port, over a fresh storage root whose
/// database holds the synthetic fixture rows (<c>reference/test/fixtures</c>). The business clock is frozen for
/// reproducible data; authentication uses native tickets and the real security clock. Every
/// data hook records its calls (<see cref="Events"/>) and can carry an armed fault (<see cref="Faults"/>).
/// </summary>
public class SliceAppFactory : WebApplicationFactory<Program>
{
    private int? _port;

    /// <summary>The synthetic fixture's business-clock instant.</summary>
    public static readonly DateTimeOffset VectorNow = DateTimeOffset.Parse( "2026-09-26T13:14:52Z", CultureInfo.InvariantCulture );

    public SliceAppFactory( )
        : this( LoadRailsFixtures )
    {
    }

    /// <summary>
    /// For suites that fill the database themselves: <paramref name="loadDatabase"/> gets the new database (schema
    /// loaded) and the frozen clock, instead of the Rails fixtures. Without it there is no database, and the app's boot
    /// loads the schema into an empty one, as a fresh install has before its first run.
    /// </summary>
    protected SliceAppFactory( Action<SqliteConnection, FrozenTimeProvider>? loadDatabase )
    {
        if ( loadDatabase is not null )
        {
            CreateDatabase( loadDatabase );
        }

        UseKestrel( );
    }

    /// <summary>The loopback port the server bound (<see cref="BoundPort"/>); starts the server.</summary>
    public int Port
    {
        get
        {
            StartServer( );
            _port ??= BoundPort.Of( Services );
            return _port.Value;
        }
    }

    public Uri BaseAddress => new( $"http://127.0.0.1:{Port}" );

    /// <summary>The origin browsers send on same-origin writes and WebSocket upgrades.</summary>
    public string Origin => $"http://127.0.0.1:{Port}";

    /// <summary>The cable's WebSocket address.</summary>
    public Uri CableAddress => new( $"ws://127.0.0.1:{Port}{CableProtocol.DefaultMountPath}" );

    public string StoragePath { get; } = TemporaryStorage.Create( );

    /// <summary><c>storage/db/production.sqlite3</c>.</summary>
    public string DatabasePath => Path.Combine( StoragePath, "db", "production.sqlite3" );

    public FrozenTimeProvider Clock { get; } = new( VectorNow );

    public SliceEvents Events { get; } = new( );

    public SliceFaults Faults { get; } = new( );

    public SliceNetwork Network { get; } = new( );

    /// <summary>The running app's shared services.</summary>
    public CampfireApp App
    {
        get
        {
            StartServer( );
            return Services.GetRequiredService<CampfireApp>( );
        }
    }

    /// <summary>A fixture's id, by label (<c>ActiveRecord::FixtureSet.identify</c>).</summary>
    public static long Id( string label ) => RailsFixtures.Identify( label );

    /// <summary>A browser-like client: its own cookie jar, no automatic redirects.</summary>
    public SliceClient Client( )
    {
        StartServer( );
        return new SliceClient( this );
    }

    /// <summary>A plain connection to the database file, outside the app's leases.</summary>
    public SqliteConnection OpenSideConnection( )
    {
        var connection = new SqliteConnection( $"Data Source={DatabasePath};Pooling=False;Default Timeout=5" );
        connection.Open( );
        return connection;
    }

    /// <summary>Runs raw SQL on a side connection (to set a state up before, or under, the app).</summary>
    public int Execute( string sql )
    {
        using var connection = OpenSideConnection( );
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        return command.ExecuteNonQuery( );
    }

    /// <summary>A single value from raw SQL on a side connection.</summary>
    public T? Scalar<T>( string sql )
    {
        using var connection = OpenSideConnection( );
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        var value = command.ExecuteScalar( );
        return value is null or DBNull ? default : ( T ) Convert.ChangeType( value, typeof( T ), CultureInfo.InvariantCulture );
    }

    protected override void ConfigureWebHost( IWebHostBuilder builder )
    {
        ArgumentNullException.ThrowIfNull( builder );

        builder.UseSetting( "CAMPFIRE_STORAGE_PATH", StoragePath );
        builder.UseSetting( "CAMPFIRE_PUBLIC_PATH", RepositoryPaths.ReferencePath( "public" ) );
        builder.UseSetting( "PORT", "0" );
        builder.UseSetting( "DISABLE_SSL", "1" );
        builder.UseSetting( "APP_VERSION", "slice" );
        builder.UseSetting( "RAILS_LOG_LEVEL", "warn" );
        builder.UseSetting( "JOB_CONCURRENCY", "1" );

        builder.ConfigureTestServices( services =>
        {
            services.AddSingleton<TimeProvider>( Clock );
            services.AddHttpContextAccessor( );
            services.AddSingleton( provider => SliceHooks.Create( provider, Events, Faults ) );
            services.AddSingleton( Network.Outbound );
        } );
    }

    protected override void Dispose( bool disposing )
    {
        base.Dispose( disposing );
        TemporaryStorage.Delete( StoragePath );
    }

    /// <summary>Every fixture file (<c>fixtures :all</c>).</summary>
    private static void LoadRailsFixtures( SqliteConnection connection, FrozenTimeProvider clock )
    {
        var fixtures = RepositoryPaths.RailsFixturesDirectory;
        RailsFixtures.Load( connection, fixtures, clock.GetUtcNow( ).UtcDateTime, Passwords.MinimumIterations );
    }

    /// <summary>The schema, then the suite's rows, into <c>storage/db/production.sqlite3</c>.</summary>
    private void CreateDatabase( Action<SqliteConnection, FrozenTimeProvider> loadDatabase )
    {
        Directory.CreateDirectory( Path.GetDirectoryName( DatabasePath )! );
        using var connection = SqliteConnections.Open( DatabasePath, reader: false );
        SchemaSetup.Prepare( connection, "production", Clock );
        loadDatabase( connection, Clock );
    }
}
