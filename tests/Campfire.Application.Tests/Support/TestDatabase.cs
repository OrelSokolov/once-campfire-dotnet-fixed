using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Application.Tests.Fixtures;
using Campfire.Application.Users;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Support;

/// <summary>
/// A real SQLite database file in its own temporary directory, opened through <see cref="CampfireDatabase"/> with a
/// controllable clock and recording hooks, and (by default) loaded with the Rails fixtures: <c>fixtures :all</c> plus
/// <c>ActiveSupport::Testing::TimeHelpers</c>.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private TestDatabase( string directory, CampfireDatabase database, TestClock clock, RecordingHooks hooks, LoadedFixtures? fixtures )
    {
        Directory = directory;
        Database = database;
        Clock = clock;
        Hooks = hooks;
        Fixtures = fixtures;
    }

    public string Directory { get; }

    public CampfireDatabase Database { get; }

    public TestClock Clock { get; }

    public RecordingHooks Hooks { get; }

    public LoadedFixtures? Fixtures { get; }

    public BasicBodyText BodyText { get; } = new( );

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A fresh database, with the fixtures unless <paramref name="fixtures"/> is false, and the background checkpointer
    /// on <paramref name="checkpoints"/> when given.
    /// </summary>
    public static async Task<TestDatabase> CreateAsync( bool fixtures = true, int readers = 2, CheckpointSettings? checkpoints = null )
    {
        var directory = NewDirectory( );
        var clock = new TestClock( );
        var hooks = new RecordingHooks( );
        var bodyText = new BasicBodyText( );
        var options = new DatabaseOptions
        {
            StorageRoot = directory,
            Environment = "test",
            ReaderCount = readers,
            Checkpoints = checkpoints ?? CheckpointSettings.Default,
        };
        var database = CampfireDatabase.Open( options, clock, hooks.ToHooks( bodyText ) );

        LoadedFixtures? loaded = null;
        if ( fixtures )
        {
            using var lease = await database.AcquireWriterAsync( Cancellation );
            loaded = RailsFixtures.Load( lease.Db.Connection, RepositoryPaths.RailsFixturesDirectory, clock.Now, Passwords.MinimumIterations );
        }

        return new TestDatabase( directory, database, clock, hooks, loaded );
    }

    /// <summary>A fresh, empty temporary directory for a database.</summary>
    public static string NewDirectory( )
    {
        var directory = Path.Combine( Path.GetTempPath( ), "campfire-tests", Guid.NewGuid( ).ToString( "N" ) );
        System.IO.Directory.CreateDirectory( directory );
        return directory;
    }

    /// <summary>A fixture's id, by label.</summary>
    public static long Id( string label ) => RailsFixtures.Identify( label );

    public Task<T> WriteAsync<T>( Func<WriteScope, T> work ) => Database.WriteAsync( work, Cancellation );

    public Task WriteAsync( Action<WriteScope> work ) => Database.WriteAsync( work, Cancellation );

    public Task<T> ReadAsync<T>( Func<CampfireDb, T> read ) => Database.ReadAsync( read, Cancellation );

    /// <summary>Runs <paramref name="work"/> under the writer lease, for operations that span transactions.</summary>
    public async Task<T> WithWriterAsync<T>( Func<WriterLease, T> work )
    {
        using var lease = await Database.AcquireWriterAsync( Cancellation );
        return work( lease );
    }

    /// <summary>
    /// Posts a message as <c>room.messages.create!</c> does: by <paramref name="creator"/> in <paramref name="room"/>
    /// (fixture labels), with a random client message id unless one is given.
    /// </summary>
    public async Task<Message> PostMessageAsync( string room, string creator, string? body, string? clientMessageId = null )
    {
        var message = new NewMessage
        {
            RoomId = Id( room ),
            CreatorId = Id( creator ),
            ClientMessageId = clientMessageId,
            Body = body,
        };

        return (await WriteAsync( scope => MessageOperations.Create( scope, message ) )).Message;
    }

    /// <summary>A single value from raw SQL on a reader; <c>@p0</c>, <c>@p1</c>, … name the parameters.</summary>
    public Task<T?> ScalarAsync<T>( string sql, params object?[] parameters ) => ReadAsync( db => Sql.Scalar<T>( db, sql, parameters ) );

    /// <summary><c>SELECT COUNT(*) FROM <paramref name="table"/></c>, filtered by <paramref name="where"/> when given.</summary>
    public async Task<int> CountAsync( string table, string? where = null, params object?[] parameters ) =>
        ( int ) (await ScalarAsync<long>( where is null ? $"SELECT COUNT(*) FROM {table}" : $"SELECT COUNT(*) FROM {table} WHERE {where}", parameters ));

    /// <summary>Runs a statement in its own write transaction on the writer lease.</summary>
    public Task<int> ExecuteAsync( string sql, params object?[] parameters ) => WriteAsync( scope => Sql.Execute( scope.Db, sql, parameters ) );

    /// <summary>Opens a separate plain connection to the same file, outside the leases.</summary>
    public SqliteConnection OpenSideConnection( )
    {
        var connection = new SqliteConnection( $"Data Source={Database.Path};Pooling=False;Default Timeout=1" );
        connection.Open( );
        return connection;
    }

    public void Dispose( )
    {
        Database.Dispose( );
        try
        {
            System.IO.Directory.Delete( Directory, recursive: true );
        }
        catch ( IOException )
        {
            // A leftover temporary directory does not affect other tests.
        }
    }
}
