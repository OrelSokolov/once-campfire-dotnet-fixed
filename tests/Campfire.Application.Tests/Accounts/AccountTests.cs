using Campfire.Application.Accounts;
using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Application.Tests.Support;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Accounts;

/// <summary><c>test/models/account_test.rb</c>, <c>account/joinable_test.rb</c>, <c>first_run_test.rb</c>, and Rust's ports.</summary>
public sealed class AccountTests
{
    private const string RestrictKey = AccountSettings.RestrictRoomCreationToAdministratorsKey;

    private static Task<Account> SignalAsync( TestDatabase testDatabase ) => testDatabase.ReadAsync( db => AccountQueries.First( db )! );

    private static Task<Account> UpdateSettingsAsync( TestDatabase testDatabase, string value ) => testDatabase.WriteAsync( scope =>
        AccountOperations.Update(
            scope,
            TestDatabase.Id( "signal" ),
            new AccountChanges { Settings = new Dictionary<string, string?> { [RestrictKey] = value } } ) );

    [Fact]
    public async Task Settings_cast_booleans_and_write_rails_json( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        Assert.Null( (await SignalAsync( testDatabase )).Settings );
        Assert.False( AccountSettings.Read( null ).RestrictRoomCreationToAdministrators );

        await UpdateSettingsAsync( testDatabase, "true" );
        var account = await SignalAsync( testDatabase );
        Assert.Equal( "{\"restrict_room_creation_to_administrators\":true}", account.Settings );
        Assert.True( AccountSettings.Read( account.Settings ).RestrictRoomCreationToAdministrators );

        await UpdateSettingsAsync( testDatabase, "false" );
        account = await SignalAsync( testDatabase );
        Assert.Equal( "{\"restrict_room_creation_to_administrators\":false}", account.Settings );
        Assert.False( AccountSettings.Read( account.Settings ).RestrictRoomCreationToAdministrators );
    }

    [Fact]
    public async Task Unknown_settings_are_rejected_and_nothing_is_written( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        await Assert.ThrowsAsync<ArgumentException>( ( ) => testDatabase.WriteAsync( scope =>
            AccountOperations.Update(
                scope,
                TestDatabase.Id( "signal" ),
                new AccountChanges { Settings = new Dictionary<string, string?> { ["nope"] = "1" } } ) ) );

        Assert.Null( (await SignalAsync( testDatabase )).Settings );
    }

    [Fact]
    public async Task Any_save_writes_the_settings_defaults_into_a_null_column( )
    {
        // has_json's `before_save -> { settings }` materializes the defaults (activemodel/lib/active_model/schematized_json.rb).
        using var testDatabase = await TestDatabase.CreateAsync( );

        await testDatabase.WriteAsync( scope => AccountOperations.Update( scope, TestDatabase.Id( "signal" ), new AccountChanges { Name = "X" } ) );

        var account = await SignalAsync( testDatabase );
        Assert.Equal( "X", account.Name );
        Assert.Equal( "{\"restrict_room_creation_to_administrators\":false}", account.Settings );
    }

    [Fact]
    public async Task Settings_keep_stored_keys_and_add_missing_defaults_after_them( )
    {
        var settings = AccountSettings.Read( "{\"theme\":\"<dark>\"}" );

        Assert.Equal( "{\"theme\":\"<dark>\",\"restrict_room_creation_to_administrators\":false}", settings.ToJson( ) );
        Assert.Equal( "\"a\\u003cb\\u003e\\u0026\\n\x2028\"", AccountSettings.EncodeString( "a<b>&\n\x2028" ) );
        Assert.Equal(
            "{\"restrict_room_creation_to_administrators\": true}",
            AccountSettings.Materialize( "{\"restrict_room_creation_to_administrators\": true}" ) );
        Assert.Equal( "null", AccountSettings.CastBoolean( "" ) );
        Assert.Equal( "false", AccountSettings.CastBoolean( "off" ) );
        Assert.Equal( "true", AccountSettings.CastBoolean( "yes" ) );
    }

    [Fact]
    public async Task New_accounts_get_a_joinable_code_and_the_settings_defaults( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.ExecuteAsync( "DELETE FROM accounts" );

        var account = await testDatabase.WriteAsync( scope => AccountOperations.Create( scope, "Chat" ) );

        Assert.Matches( @"^[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}$", account.JoinCode );
        Assert.Equal( "{\"restrict_room_creation_to_administrators\":false}", account.Settings );
        Assert.Equal( 0, account.SingletonGuard );
    }

    [Fact]
    public async Task Only_one_account_can_exist( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        await Assert.ThrowsAsync<RecordNotUniqueException>( ( ) => testDatabase.WriteAsync( scope => AccountOperations.Create( scope, "Second" ) ) );
    }

    [Fact]
    public async Task Accounts_can_reset_their_join_code( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var before = (await SignalAsync( testDatabase )).JoinCode;

        await testDatabase.WriteAsync( scope => AccountOperations.ResetJoinCode( scope, TestDatabase.Id( "signal" ) ) );

        Assert.NotEqual( before, (await SignalAsync( testDatabase )).JoinCode );
    }

    [Fact]
    public async Task First_run_creates_an_administrator_with_access_to_the_first_open_room( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );

        var user = await testDatabase.WithWriterAsync( lease => FirstRun.Create( lease, new NewUser
        {
            Name = "User",
            EmailAddress = "user@example.com",
            PasswordDigest = Passwords.Digest( "secret123456", Passwords.MinimumIterations ),
        } ) );

        var rooms = await testDatabase.ReadAsync( db => RoomQueries.ForUser( db, user.Id ).ToList( ) );
        var membership = await testDatabase.ReadAsync( db => MembershipQueries.ForUser( db, user.Id ).Single( ) );
        Assert.True( user.IsAdministrator );
        Assert.Equal( ["All Talk"], rooms.Select( room => room.Name ) );
        Assert.Equal( 1, await testDatabase.CountAsync( "memberships" ) );
        Assert.True( (await testDatabase.ReadAsync( RoomQueries.Original ))!.IsOpen );
        Assert.Equal( Involvement.Mentions, membership.Involvement );
        Assert.Equal( FirstRun.AccountName, (await testDatabase.ReadAsync( AccountQueries.First ))!.Name );
        Assert.Equal(
            user.Id,
            (await UserAuthentication.AuthenticateByAsync( testDatabase.Database, "user@example.com", "secret123456", TestDatabase.Cancellation ))?.Id );
    }

    [Fact]
    public async Task A_second_first_run_fails_on_the_account_singleton( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var administrator = new NewUser { Name = "User", EmailAddress = "user@example.com" };
        await testDatabase.WithWriterAsync( lease => FirstRun.Create( lease, administrator ) );

        await Assert.ThrowsAsync<RecordNotUniqueException>( ( ) => testDatabase.WithWriterAsync( lease => FirstRun.Create(
            lease,
            administrator with { EmailAddress = "other@example.com" } ) ) );

        Assert.Equal( 1, await testDatabase.CountAsync( "users" ) );
    }
}
