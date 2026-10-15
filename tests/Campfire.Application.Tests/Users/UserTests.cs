using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Application.Tests.Support;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Users;

/// <summary><c>test/models/user_test.rb</c>, <c>user/role_test.rb</c>, and Rust's <c>user_test.rs</c>.</summary>
public sealed class UserTests
{
    private static NewUser NewMember => new( )
    {
        Name = "User",
        EmailAddress = "user@example.com",
        PasswordDigest = Passwords.Digest( "secret123456", Passwords.MinimumIterations ),
    };

    private static Task<User> UserAsync( TestDatabase testDatabase, string label ) =>
        testDatabase.ReadAsync( db => UserQueries.Find( db, TestDatabase.Id( label ) ) );

    [Fact]
    public async Task Users_accept_very_long_passwords( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var password = string.Concat( Enumerable.Repeat( "secret", 50 ) );

        await testDatabase.WriteAsync( scope => UserOperations.Update(
            scope,
            TestDatabase.Id( "david" ),
            new UserChanges { PasswordDigest = Passwords.Digest( password, Passwords.MinimumIterations ) } ) );

        Assert.True( Passwords.Verify( password, (await UserAsync( testDatabase, "david" )).PasswordDigest ) );
    }

    [Fact]
    public async Task Creating_a_user_grants_membership_to_the_open_rooms_after_commit( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var before = await testDatabase.CountAsync( "memberships" );
        var openRooms = await testDatabase.CountAsync( "rooms", "type = @p0", RoomTypes.OpenClassName );

        var user = await testDatabase.WriteAsync( scope => UserOperations.Create( scope, NewMember ) );

        Assert.Equal( before + openRooms, await testDatabase.CountAsync( "memberships" ) );
        Assert.All(
            await testDatabase.ReadAsync( db => MembershipQueries.ForUser( db, user.Id ) ),
            membership => Assert.Equal( Involvement.Mentions, membership.Involvement ) );
    }

    [Fact]
    public async Task Creating_subsequent_users_makes_them_active_members_with_identity_v3_passwords( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        var user = await testDatabase.WriteAsync( scope => UserOperations.Create( scope, NewMember ) );

        Assert.True( user.IsMember );
        Assert.True( user.IsActive );
        Assert.True( Passwords.Verify( "secret123456", user.PasswordDigest ) );
        Assert.Equal( ( byte ) 0x01, Convert.FromBase64String( user.PasswordDigest! )[0] );
    }

    [Fact]
    public async Task A_duplicate_email_address_is_record_not_unique( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        await Assert.ThrowsAsync<RecordNotUniqueException>( ( ) => testDatabase.WriteAsync( scope => UserOperations.Create(
            scope,
            NewMember with { EmailAddress = "david@37signals.com" } ) ) );
    }

    [Fact]
    public async Task Creating_a_user_with_an_avatar_uploads_grants_then_analyzes( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var avatar = new NewAttachment
        {
            Key = "avatarkey",
            Filename = "me.png",
            ContentType = "image/png",
            ServiceName = "local",
            ByteSize = 1,
            UploadSource = "me.png",
        };
        long? membershipsAtUpload = null;
        using var side = testDatabase.OpenSideConnection( );
        testDatabase.Hooks.Intercept = line =>
        {
            if ( line.StartsWith( "upload", StringComparison.Ordinal ) )
            {
                using var command = side.CreateCommand( );
                command.CommandText = "SELECT COUNT(*) FROM memberships";
                membershipsAtUpload = ( long ) command.ExecuteScalar( )!;
            }
        };
        var before = await testDatabase.CountAsync( "memberships" );

        var user = await testDatabase.WriteAsync( scope => UserOperations.Create( scope, NewMember with { Avatar = avatar } ) );

        var blob = await testDatabase.ReadAsync( db => Sql.Column( db, "SELECT id FROM active_storage_blobs" ).Single( ) );
        Assert.Equal( [$"upload blob {blob} from me.png", $"analyze blob {blob}"], testDatabase.Hooks.Events );
        Assert.Equal( before, membershipsAtUpload );
        Assert.True( await testDatabase.CountAsync( "memberships" ) > before );
        var attachment = await testDatabase.ReadAsync( db =>
            Sql.Column( db, "SELECT record_type || ' ' || record_id || ' ' || name FROM active_storage_attachments" ).Single( ) );
        Assert.Equal( $"User {user.Id} avatar", attachment );
    }

    [Fact]
    public async Task Deactivating_deletes_non_direct_memberships_push_subscriptions_searches_and_sessions_and_rewrites_the_email( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var david = TestDatabase.Id( "david" );
        var memberships = await testDatabase.CountAsync( "memberships" );
        var withoutDirects = await testDatabase.ReadAsync( db => MembershipQueries.CountWithoutDirectRooms( db, david ) );
        var subscriptions = await testDatabase.CountAsync( "push_subscriptions" );
        var searches = await testDatabase.CountAsync( "searches" );

        await testDatabase.WriteAsync( scope => UserOperations.Deactivate( scope, david ) );

        var reloaded = await UserAsync( testDatabase, "david" );
        Assert.Equal( memberships - withoutDirects, await testDatabase.CountAsync( "memberships" ) );
        Assert.Equal( subscriptions - 1, await testDatabase.CountAsync( "push_subscriptions" ) );
        Assert.Equal( searches - 1, await testDatabase.CountAsync( "searches" ) );
        Assert.Equal( 0, await testDatabase.CountAsync( "sessions", "user_id = @p0", david ) );
        Assert.Matches( @"^david-deactivated-[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}@37signals\.com$", reloaded.EmailAddress );
        Assert.Equal( UserStatus.Deactivated, reloaded.Status );
    }

    [Fact]
    public async Task Deactivating_closes_connections_after_commit( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var david = TestDatabase.Id( "david" );
        using var side = testDatabase.OpenSideConnection( );
        var statusAtDisconnect = new List<long>( );
        testDatabase.Hooks.Intercept = _ =>
        {
            using var command = side.CreateCommand( );
            command.CommandText = $"SELECT status FROM users WHERE id = {david}";
            statusAtDisconnect.Add( ( long ) command.ExecuteScalar( )! );
        };

        await testDatabase.WriteAsync( scope => UserOperations.Deactivate( scope, david ) );

        Assert.Equal( [$"disconnect user {david} reconnect=false"], testDatabase.Hooks.Events );
        Assert.Equal( [( long ) UserStatus.Deactivated], statusAtDisconnect );
    }

    [Fact]
    public void Initials_and_title_come_from_the_name_and_a_non_blank_bio( )
    {
        var jz = new User { Name = "JZ", Bio = "Designer" };

        Assert.Equal( "J", jz.Initials );
        Assert.Equal( "JZ – Designer", jz.Title );
        Assert.Equal( "BB", new User { Name = "Bender Bot" }.Initials );
        Assert.Equal( "Z", new User { Name = "Émile Zola" }.Initials );
        Assert.Equal( "Émile Zola", new User { Name = "Émile Zola", Bio = "  " }.Title );
    }

    [Fact]
    public async Task Administrators_can_administer_anything_and_members_only_their_own_or_new_records( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var kevin = await UserAsync( testDatabase, "kevin" );
        var designers = await testDatabase.ReadAsync( db => RoomQueries.FindById( db, TestDatabase.Id( "designers" ) )! );

        Assert.True( new User { Role = UserRole.Administrator }.CanAdminister( ) );
        Assert.False( new User { Role = UserRole.Member }.CanAdminister( ) );
        Assert.True( kevin.CanAdminister( kevin.Id ) );
        Assert.True( kevin.CanAdminister( TestDatabase.Id( "jz" ), recordIsNew: true ) );
        Assert.False( kevin.CanAdminister( designers.CreatorId ) );
    }

    [Fact]
    public async Task Authenticate_by_email_and_password( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        Assert.Equal(
            TestDatabase.Id( "david" ),
            (await UserAuthentication.AuthenticateByAsync( testDatabase.Database, "david@37signals.com", "secret123456", TestDatabase.Cancellation ))?.Id );
        Assert.Null( await UserAuthentication.AuthenticateByAsync( testDatabase.Database, "david@37signals.com", "wrong", TestDatabase.Cancellation ) );
        Assert.Null( await UserAuthentication.AuthenticateByAsync( testDatabase.Database, "nobody@37signals.com", "secret123456", TestDatabase.Cancellation ) );
        Assert.Null( await UserAuthentication.AuthenticateByAsync( testDatabase.Database, "david@37signals.com", "", TestDatabase.Cancellation ) );
        Assert.Null( await UserAuthentication.AuthenticateByAsync( testDatabase.Database, null, "secret123456", TestDatabase.Cancellation ) );
    }

    [Fact]
    public async Task Deactivated_users_cannot_authenticate( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.WriteAsync( scope => UserOperations.Deactivate( scope, TestDatabase.Id( "jason" ) ) );

        Assert.Null( await UserAuthentication.AuthenticateByAsync( testDatabase.Database, "jason@37signals.com", "secret123456", TestDatabase.Cancellation ) );
    }

    [Fact]
    public async Task Ordered_scopes_sort_by_lower_name( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        Assert.Equal(
            ["Bender Bot", "David", "Jason", "JZ", "Kevin"],
            (await testDatabase.ReadAsync( UserQueries.ActiveOrdered )).Select( user => user.Name ) );
        Assert.Equal( ["David", "Jason", "JZ", "Kevin"], (await testDatabase.ReadAsync( UserQueries.ActiveOrderedWithoutBots )).Select( user => user.Name ) );
        Assert.Equal( ["Bender Bot"], (await testDatabase.ReadAsync( UserQueries.ActiveBotsOrdered )).Select( user => user.Name ) );
        Assert.Equal( ["Jason", "JZ"], (await testDatabase.ReadAsync( db => UserQueries.ActiveFilteredBy( db, "j", 20 ) )).Select( user => user.Name ) );
    }
}
