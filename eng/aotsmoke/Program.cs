using Campfire.AotSmoke;
using Campfire.Application.Accounts;
using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Integrations.Search;
using Campfire.Application.Memberships;
using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Application.Storage;
using Campfire.Application.Users;
using Microsoft.Data.Sqlite;

// Runs the data layer's main paths in a Native AOT binary against a fresh database in a temporary directory,
// printing PASS or FAIL per step; the exit code is the number of failures.
var root = Path.Combine( Path.GetTempPath( ), $"campfire-aotsmoke-{Guid.NewGuid( ):N}" );
var hooks = new SmokeHooks( );
var options = new DatabaseOptions { StorageRoot = root, Environment = "smoke", ReaderCount = 2 };
var cancellationToken = CancellationToken.None;
var failures = 0;

void Step( string name, Func<string?> check )
{
    try
    {
        var detail = check( );
        Console.WriteLine( detail is null ? $"PASS {name}" : $"PASS {name}: {detail}" );
    }
    catch ( Exception error )
    {
        failures++;
        Console.WriteLine( $"FAIL {name}: {error.GetType( ).Name}: {error.Message}" );
    }
}

void Expect( bool condition, string what )
{
    if ( !condition )
    {
        throw new InvalidOperationException( $"expected {what}" );
    }
}

T Await<T>( Task<T> task ) => task.GetAwaiter( ).GetResult( );

CampfireDatabase? database = null;
Step( "open a new database and load schema.sql", ( ) =>
{
    database = CampfireDatabase.Open( options, TimeProvider.System, hooks.ToHooks( ) );
    Expect( database.Preparation == SchemaPreparation.Loaded, "the schema to be loaded" );
    return database.Path;
} );

if ( database is null )
{
    return 1;
}

var db = database;
User administrator = null!;
User member = null!;
Room room = null!;
List<Message> messages = [];

Step( "first run: account, administrator and the first room", ( ) =>
{
    using var lease = Await( db.AcquireWriterAsync( cancellationToken ) );
    administrator = FirstRun.Create( lease, new NewUser
    {
        Name = "Ada Admin",
        EmailAddress = "ada@example.com",
        PasswordDigest = Passwords.Digest( "secret123456", Passwords.MinimumIterations ),
    } );
    var account = AccountQueries.First( lease.Db );
    Expect( account?.Name == FirstRun.AccountName, "the Campfire account" );
    Expect( administrator.IsAdministrator, "an administrator" );
    return $"user {administrator.Id}, join code {account!.JoinCode}";
} );

Step( "create a member, granted the open rooms after commit", ( ) =>
{
    member = Await( db.WriteAsync(
        scope => UserOperations.Create( scope, new NewUser { Name = "Mel Member", EmailAddress = "mel@example.com" } ),
        cancellationToken ) );
    var rooms = Await( db.ReadAsync( read => RoomQueries.ForUser( read, member.Id ), cancellationToken ) );
    Expect( rooms.Count == 1 && rooms[0].IsOpen, "the member in the open room" );
    return $"user {member.Id}";
} );

Step( "authenticate by email and password", ( ) =>
{
    var user = Await( UserAuthentication.AuthenticateByAsync( db, "ada@example.com", "secret123456", cancellationToken ) );
    Expect( user?.Id == administrator.Id, "the administrator" );
    return null;
} );

Step( "create a closed room for both users", ( ) =>
{
    room = Await( db.WriteAsync(
        scope => RoomOperations.CreateFor( scope, RoomType.Closed, "Smoke", administrator.Id, [administrator.Id, member.Id] ),
        cancellationToken ) );
    var members = Await( db.ReadAsync( read => RoomQueries.UserIds( read, room.Id ), cancellationToken ) );
    Expect( members.Count == 2, "two members" );
    return $"room {room.Id}";
} );

Step( "post 45 messages: rows, bodies, index and unread after commit", ( ) =>
{
    hooks.Clear( );
    for ( var number = 0; number < 45; number++ )
    {
        var body = $"<div>Hello <strong>hovercraft</strong> number {number}</div>";
        var created = Await( db.WriteAsync(
            scope => MessageOperations.Create( scope, new NewMessage { RoomId = room.Id, CreatorId = administrator.Id, Body = body } ),
            cancellationToken ) );
        messages.Add( created.Message );
    }

    var indexed = Await( db.ReadAsync( read => MessageSearchIndex.IndexedBody( read, messages[0].Id ), cancellationToken ) );
    var unread = Await( db.ReadAsync( read => MembershipQueries.UnreadCount( read, member.Id ), cancellationToken ) );
    Expect( indexed == "Hello hovercraft number 0", $"the plain text indexed, not \"{indexed}\"" );
    Expect( unread == 1, "the member's room unread" );
    Expect( hooks.Events.Count( hookEvent => hookEvent.StartsWith( "push ", StringComparison.Ordinal ) ) == 45, "a push job per message" );
    return $"{messages.Count} messages";
} );

Step( "stored timestamps are Rails text", ( ) =>
{
    var stored = Await( db.ReadAsync( read =>
    {
        using var command = read.Connection.CreateCommand( );
        command.CommandText = "SELECT created_at || ' ' || typeof(created_at) FROM messages ORDER BY id LIMIT 1";
        return ( string ) command.ExecuteScalar( )!;
    }, cancellationToken ) );
    Expect( stored.EndsWith( " text", StringComparison.Ordinal ) && stored[19] is '.' or ' ', "YYYY-MM-DD HH:MM:SS[.ffffff] text" );
    return stored;
} );

Step( "message pages are bounded and ordered", ( ) =>
{
    var last = Await( db.ReadAsync( read => MessagePages.LastPage( read, room.Id ), cancellationToken ) );
    var first = Await( db.ReadAsync( read => MessagePages.FirstPage( read, room.Id ), cancellationToken ) );
    var around = Await( db.ReadAsync( read => MessagePages.PageAround( read, room.Id, messages[22] ), cancellationToken ) );
    var paged = Await( db.ReadAsync( read => MessagePages.IsPaged( read, room.Id ), cancellationToken ) );
    Expect( last.Count == MessagePages.PageSize && last[^1].Id == messages[^1].Id, "the newest 40, oldest first" );
    Expect( first[0].Id == messages[0].Id, "the oldest first" );
    Expect( around.Count == 45 && around[22].Id == messages[22].Id, "the page around a message" );
    Expect( paged, "the room paged" );
    return $"last {last.Count}, first {first.Count}, around {around.Count}";
} );

Step( "search the member's rooms (FTS5) and record the query", ( ) =>
{
    var results = Await( db.ReadAsync( read => MessageSearch.Find( read, member.Id, "hovercraft" ), cancellationToken ) );
    Await( db.WriteAsync( scope => MessageSearch.Record( scope, member.Id, "hovercraft" ), cancellationToken ) );
    var recent = Await( db.ReadAsync( read => MessageSearch.Recent( read, member.Id ), cancellationToken ) );
    Expect( results.Messages.Count == 45, "every message found" );
    Expect( recent.Count == 1 && recent[0].Query == "hovercraft", "the recorded search" );
    return $"{results.Messages.Count} matches";
} );

Step( "boost, sidebar and presence", ( ) =>
{
    var boost = Await( db.WriteAsync( scope => BoostOperations.Create( scope, messages[0].Id, member.Id, "🔥" ), cancellationToken ) );
    var boosts = Await( db.ReadAsync( read => BoostOperations.ForMessages( read, [messages[0].Id, messages[1].Id] ), cancellationToken ) );
    var sidebar = Await( db.ReadAsync( read => MembershipQueries.Sidebar( read, member.Id ), cancellationToken ) );
    var membership = Await( db.ReadAsync( read => MembershipQueries.FindByRoomAndUser( read, room.Id, member.Id ), cancellationToken ) );
    var present = Await( db.WriteAsync( scope => MembershipPresence.Present( scope, membership!.Id ), cancellationToken ) );
    Expect( boosts.Count == 1 && boosts[0].Id == boost.Id, "the boost" );
    Expect( sidebar.OtherMemberships.Count == 2, "two rooms in the sidebar" );
    Expect( present.Connections == 1 && present.UnreadAt is null, "the member present" );
    return null;
} );

Step( "update a user: only changed columns", ( ) =>
{
    var updated = Await( db.WriteAsync( scope => UserOperations.Update( scope, member.Id, new UserChanges { Bio = "Smoke tester" } ), cancellationToken ) );
    var unchanged = Await( db.WriteAsync( scope => UserOperations.Update( scope, member.Id, new UserChanges { Bio = "Smoke tester" } ), cancellationToken ) );
    Expect( updated.Bio == "Smoke tester" && unchanged.UpdatedAt == updated.UpdatedAt, "the bio saved once" );
    return null;
} );

Step( "a failed write rolls back and runs no after-commit work", ( ) =>
{
    hooks.Clear( );
    var before = Await( db.ReadAsync( read => MessageQueries.CountInRoom( read, room.Id ), cancellationToken ) );
    try
    {
        Await( db.WriteAsync<Message>( scope =>
        {
            MessageOperations.Create( scope, new NewMessage { RoomId = room.Id, CreatorId = member.Id, Body = "never" } );
            throw new InvalidOperationException( "roll back" );
        }, cancellationToken ) );
        throw new InvalidOperationException( "the write did not fail" );
    }
    catch ( InvalidOperationException error ) when ( error.Message == "roll back" )
    {
        // The failure the step expects.
    }

    var after = Await( db.ReadAsync( read => MessageQueries.CountInRoom( read, room.Id ), cancellationToken ) );
    Expect( after == before, "no new message" );
    Expect( hooks.Events.Count == 0, "no after-commit work" );
    return $"{after} messages";
} );

Step( "a unique violation is RecordNotUnique", ( ) =>
{
    try
    {
        Await( db.WriteAsync( scope => UserOperations.Create( scope, new NewUser { Name = "Copy", EmailAddress = "mel@example.com" } ), cancellationToken ) );
    }
    catch ( RecordNotUniqueException )
    {
        return null;
    }

    throw new InvalidOperationException( "the duplicate email address was accepted" );
} );

Step( "blob records: a blob, its attachment and a variant", ( ) =>
{
    var records = new BlobRecords( db );
    var avatarBlob = new NewBlob
    {
        Key = "smoke-key",
        Filename = new BlobFilename( "moon.png" ),
        ContentType = "image/png",
        ServiceName = "local",
        ByteSize = 1,
        Checksum = "x",
    };
    var blob = Await( records.InsertBlobAsync( avatarBlob, DateTimeOffset.UtcNow, cancellationToken ) );
    var owner = UserAvatars.Owner( member.Id );
    var avatar = new UserChanges { AvatarBlob = new SignedBlobAttachment( blob.Id, Identity: null ) };
    Await( db.WriteAsync( scope => UserOperations.Update( scope, member.Id, avatar ), cancellationToken ) );
    var variantBlob = new NewBlob
    {
        Key = "smoke-variant",
        Filename = new BlobFilename( "moon.webp" ),
        ContentType = "image/webp",
        ServiceName = "local",
        ByteSize = 1,
        Checksum = "y",
    };
    var variant = Await( records.RecordVariantAsync( blob.Id, "digest", variantBlob, DateTimeOffset.UtcNow, cancellationToken ) );
    var attached = Await( records.AttachedBlobAsync( owner, UserAvatars.AttachmentName, cancellationToken ) );
    Expect( attached?.Id == blob.Id && variant is not null, "the avatar and its variant" );
    return null;
} );

Step( "checkpoint the WAL into the database file", ( ) =>
{
    Expect( Await( db.CheckpointAsync( cancellationToken ) ), "a complete TRUNCATE checkpoint" );
    Expect( new FileInfo( db.Path + "-wal" ).Length == 0, "an empty WAL" );
    return null;
} );

Step( "shut down, reopen and accept the existing database", ( ) =>
{
    db.Dispose( );
    using var reopened = CampfireDatabase.Open( options, TimeProvider.System, hooks.ToHooks( ) );
    var count = Await( reopened.ReadAsync( read => MessageQueries.CountInRoom( read, room.Id ), cancellationToken ) );
    Expect( reopened.Preparation == SchemaPreparation.Accepted, "the schema accepted" );
    Expect( count == 45, "every message kept" );
    return null;
} );

SqliteConnection.ClearAllPools( );
Directory.Delete( root, recursive: true );
Console.WriteLine( failures == 0 ? "ALL PASS" : $"{failures} FAILED" );
return failures;
