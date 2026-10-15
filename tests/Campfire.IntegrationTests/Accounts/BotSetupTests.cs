using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// Chat bot setup (<c>Accounts::BotsController</c>, <c>Accounts::Bots::KeysController</c>): administrators create, edit
/// and remove bots and reset their keys, which revokes the old key at once.
/// </summary>
public sealed class BotSetupTests
{
    private static readonly long Bender = SliceAppFactory.Id( "bender" );

    private static readonly long Watercooler = SliceAppFactory.Id( "watercooler" );

    [Fact]
    public async Task Administrators_see_each_bot_with_its_key_and_shared_rooms( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var key = BotApiKeys.Bender( factory );

        var index = await admin.GetAsync( Routes.AccountBots );
        var edit = await admin.GetAsync( Routes.EditAccountBot( Bender ) );
        var form = await admin.GetAsync( Routes.NewAccountBot );
        var missing = await admin.GetAsync( Routes.EditAccountBot( SliceAppFactory.Id( "kevin" ) ) );

        Assert.Equal( HttpStatusCode.OK, index.Status );
        Assert.Contains( "<strong>Bender Bot</strong>", index.Body, StringComparison.Ordinal );
        Assert.Contains( $"/{key}/messages", index.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, edit.Status );
        Assert.Contains( "value=\"http://example.com/bender\"", edit.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, form.Status );
        Assert.Contains( "default-bot-avatar", form.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.NotFound, missing.Status );
    }

    [Fact]
    public async Task Creating_a_bot_makes_its_token_its_webhook_and_its_open_room_memberships( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.NewAccountBot );

        var created = await admin.SubmitFormAsync(
            HttpMethod.Post,
            Routes.AccountBots,
            token,
            [
                ("user[name]", "Deploy Bot"),
                ("user[webhook_url]", "https://example.com/deploy"),
            ] );
        var bot = factory.Scalar<long>( "SELECT id FROM users WHERE name = 'Deploy Bot'" );

        Assert.Equal( HttpStatusCode.Found, created.Status );
        Assert.Equal( $"{factory.BaseAddress}account/bots", created.Location );
        Assert.Equal( "2|0", factory.Scalar<string>( $"SELECT role || '|' || status FROM users WHERE id = {bot}" ) );
        Assert.Matches( "^[A-Za-z0-9]{12}$", factory.Scalar<string>( $"SELECT bot_token FROM users WHERE id = {bot}" ) );
        Assert.Equal( "https://example.com/deploy", factory.Scalar<string>( $"SELECT url FROM webhooks WHERE user_id = {bot}" ) );
        Assert.Equal(
            factory.Scalar<long>( "SELECT COUNT(*) FROM rooms WHERE type = 'Rooms::Open'" ),
            factory.Scalar<long>( $"SELECT COUNT(*) FROM memberships WHERE user_id = {bot}" ) );
    }

    [Fact]
    public async Task Editing_a_bot_without_a_webhook_url_removes_its_webhook_and_removing_the_bot_deactivates_it( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccountBot( Bender ) );

        var updated = await admin.SubmitFormAsync(
            HttpMethod.Patch,
            Routes.AccountBot( Bender ),
            token,
            [
                ("user[name]", "Bender Rodriguez"),
                ("user[webhook_url]", ""),
            ] );
        var name = factory.Scalar<string>( $"SELECT name FROM users WHERE id = {Bender}" );
        var webhooks = factory.Scalar<long>( $"SELECT COUNT(*) FROM webhooks WHERE user_id = {Bender}" );
        var removed = await admin.SubmitFormAsync( HttpMethod.Delete, Routes.AccountBot( Bender ), token );

        Assert.Equal( HttpStatusCode.Found, updated.Status );
        Assert.Equal( "Bender Rodriguez", name );
        Assert.Equal( 0L, webhooks );
        Assert.Equal( HttpStatusCode.Found, removed.Status );
        Assert.Equal( $"{factory.BaseAddress}account/bots", removed.Location );
        Assert.Equal( 1L, factory.Scalar<long>( $"SELECT status FROM users WHERE id = {Bender}" ) );
    }

    [Fact]
    public async Task Resetting_a_key_revokes_the_old_one_and_bot_keys_do_not_authenticate_browser_pages( )
    {
        using var factory = new SliceAppFactory( );
        var oldKey = BotApiKeys.Bender( factory );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.AccountBots );
        using var bot = factory.Client( );
        var editWithOldKey = BotApiKeys.InQuery( Routes.EditAccount, oldKey );

        // The browser route ignores bot_key and challenges for a browser session. The bot API accepts only the path key.
        var before = await bot.GetAsync( editWithOldKey );
        var beforeApi = await bot.GetAsync( Routes.RoomBotMessages( Watercooler, oldKey ), "application/json" );
        var reset = await admin.SubmitFormAsync( HttpMethod.Patch, Routes.AccountBotKey( Bender ), token );
        var newKey = BotApiKeys.Bender( factory );
        var editWithNewKey = BotApiKeys.InQuery( Routes.EditAccount, newKey );
        var withOld = await bot.GetAsync( editWithOldKey );
        var withNew = await bot.GetAsync( editWithNewKey );
        var oldApi = await bot.GetAsync( Routes.RoomBotMessages( Watercooler, oldKey ), "application/json" );
        var newApi = await bot.GetAsync( Routes.RoomBotMessages( Watercooler, newKey ), "application/json" );

        SliceAssert.SignInChallenge( factory, before, editWithOldKey );
        Assert.Equal( HttpStatusCode.OK, beforeApi.Status );
        Assert.Equal( HttpStatusCode.Found, reset.Status );
        Assert.Equal( $"{factory.BaseAddress}account/bots", reset.Location );
        Assert.NotEqual( oldKey, newKey );
        SliceAssert.SignInChallenge( factory, withOld, editWithOldKey );
        SliceAssert.SignInChallenge( factory, withNew, editWithNewKey );
        Assert.Equal( HttpStatusCode.Unauthorized, oldApi.Status );
        Assert.Null( oldApi.Location );
        Assert.Equal( HttpStatusCode.OK, newApi.Status );
    }

    [Fact]
    public async Task Members_cannot_set_up_bots( )
    {
        using var factory = new SliceAppFactory( );
        using var member = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await member.CsrfTokenAsync( Routes.EditAccount );
        var bots = factory.Scalar<long>( "SELECT COUNT(*) FROM users WHERE role = 2" );

        var index = await member.GetAsync( Routes.AccountBots );
        var create = await member.SubmitFormAsync( HttpMethod.Post, Routes.AccountBots, token, [("user[name]", "Rogue")] );
        var key = await member.SubmitFormAsync( HttpMethod.Patch, Routes.AccountBotKey( Bender ), token );

        Assert.Equal( HttpStatusCode.Forbidden, index.Status );
        Assert.Equal( HttpStatusCode.Forbidden, create.Status );
        Assert.Equal( HttpStatusCode.Forbidden, key.Status );
        Assert.Equal( bots, factory.Scalar<long>( "SELECT COUNT(*) FROM users WHERE role = 2" ) );
    }
}
