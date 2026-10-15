using Campfire.Application.Bots;
using Campfire.Application.Tests.Support;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Bots;

/// <summary><c>test/models/user/bot_test.rb</c> and Rust's bot tests.</summary>
public sealed class BotTests
{
    private static Task<User> CreateBotAsync( TestDatabase testDatabase, string name, string? webhookUrl = null ) =>
        testDatabase.WithWriterAsync( lease => BotOperations.CreateBot( lease, name, webhookUrl ) );

    [Fact]
    public async Task Creating_a_bot_gives_it_a_twelve_character_token_and_no_password_or_webhook( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        var bot = await CreateBotAsync( testDatabase, "Bender" );

        Assert.Equal( 12, bot.BotToken!.Length );
        Assert.True( bot.BotToken.All( char.IsAsciiLetterOrDigit ) );
        Assert.Equal( $"{bot.Id}-{bot.BotToken}", bot.BotKey );
        Assert.Equal( UserRole.Bot, bot.Role );
        Assert.Null( bot.PasswordDigest );
        Assert.Null( await testDatabase.ReadAsync( db => BotOperations.FindWebhookByBotId( db, bot.Id ) ) );
    }

    [Fact]
    public async Task Create_bot_with_a_webhook_then_remove_it_by_updating_with_a_blank_url( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bot = await CreateBotAsync( testDatabase, "Bot", "http://x" );
        Assert.Equal( "http://x", (await testDatabase.ReadAsync( db => BotOperations.FindWebhookByBotId( db, bot.Id ) ))?.Url );

        await testDatabase.WriteAsync( scope => BotOperations.UpdateBot( scope, bot.Id, new UserChanges { Name = "Bot2" }, "" ) );

        Assert.Null( await testDatabase.ReadAsync( db => BotOperations.FindWebhookByBotId( db, bot.Id ) ) );
        Assert.Equal( "Bot2", (await testDatabase.ReadAsync( db => UserQueries.Find( db, bot.Id ) )).Name );
    }

    [Fact]
    public async Task Updating_a_bot_updates_or_creates_its_webhook( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bot = await CreateBotAsync( testDatabase, "Bot", "http://x" );
        var plain = await CreateBotAsync( testDatabase, "Plain" );

        await testDatabase.WriteAsync( scope => BotOperations.UpdateBot( scope, bot.Id, new UserChanges( ), "http://y" ) );
        await testDatabase.WriteAsync( scope => BotOperations.UpdateBot( scope, plain.Id, new UserChanges( ), "http://z" ) );

        Assert.Equal( "http://y", (await testDatabase.ReadAsync( db => BotOperations.FindWebhookByBotId( db, bot.Id ) ))?.Url );
        Assert.Equal( "http://z", (await testDatabase.ReadAsync( db => BotOperations.FindWebhookByBotId( db, plain.Id ) ))?.Url );
    }

    [Fact]
    public async Task Resetting_the_bot_key_retires_the_old_key( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bot = await CreateBotAsync( testDatabase, "Bender" );

        var reset = await testDatabase.WriteAsync( scope => BotOperations.ResetBotKey( scope, bot.Id ) );

        Assert.NotEqual( bot.BotKey, reset.BotKey );
        Assert.Null( await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, bot.BotKey ) ) );
        Assert.Equal( bot.Id, (await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, reset.BotKey ) ))?.Id );
    }

    [Fact]
    public async Task A_bot_key_authenticates_by_id_and_token_and_ignores_a_trailing_suffix( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bot = await CreateBotAsync( testDatabase, "Bender" );

        Assert.Equal( bot.Id, (await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, bot.BotKey ) ))?.Id );
        Assert.Equal( bot.Id, (await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, $"{bot.BotKey}-ignored" ) ))?.Id );
        Assert.Null( await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, "nonsense" ) ) );
        Assert.Null( await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, $"{bot.Id}-" ) ) );
        Assert.Null( await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, $"{bot.Id}" ) ) );
        Assert.Null( await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, $"{TestDatabase.Id( "david" )}-{bot.BotToken}" ) ) );
    }

    [Fact]
    public async Task Deactivated_bots_do_not_authenticate( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bot = await CreateBotAsync( testDatabase, "Bender" );

        await testDatabase.WriteAsync( scope => UserOperations.Deactivate( scope, bot.Id ) );

        Assert.Null( await testDatabase.ReadAsync( db => UserQueries.AuthenticateBot( db, bot.BotKey ) ) );
    }

    [Fact]
    public async Task Deliver_webhook_later_only_for_bots_with_a_webhook( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var first = TestDatabase.Id( "first" );

        Assert.True( await testDatabase.ReadAsync( db => BotOperations.DeliverWebhookLater( db, testDatabase.Hooks, TestDatabase.Id( "bender" ), first ) ) );
        Assert.False( await testDatabase.ReadAsync( db => BotOperations.DeliverWebhookLater( db, testDatabase.Hooks, TestDatabase.Id( "jz" ), first ) ) );

        Assert.Equal( [$"webhook bot {TestDatabase.Id( "bender" )} message {first}"], testDatabase.Hooks.Events );
    }
}
