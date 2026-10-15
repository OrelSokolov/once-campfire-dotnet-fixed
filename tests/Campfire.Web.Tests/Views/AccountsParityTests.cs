using System.Text.Json.Nodes;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Models;
using static Campfire.Web.Tests.Views.Support.PageGoldenFacts;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>DOM parity of the <c>accounts/*</c> views with the Rails goldens (Rust <c>parity_a.rs</c>).</summary>
public sealed class AccountsParityTests
{
    [Theory]
    [InlineData( "account_edit_admin", null )]
    [InlineData( "account_edit_member", null )]
    [InlineData( "account_edit_notice", "✓" )]
    [InlineData( "account_edit_paginated", null )]
    [InlineData( "account_edit_with_logo", null )]
    [InlineData( "account_edit_with_logo_member", null )]
    public async Task Edit_matches_rails( string name, string? notice )
    {
        var viewContext = Context( name, new PageGoldenRequest( FlashNotice: notice ) );

        // AccountsController#account_users
        var visible = OrderedUsers( name )
            .Where( user => !user.IsBot )
            .Where( user => viewContext.CanAdminister ? user.Active || user.Banned : user.Active )
            .ToList( );
        var model = new AccountEditPage(
            viewContext,
            Account( name )["id"].RequiredLong( ),
            Account( name )["join_code"].RequiredString( ),
            Account( name )["restrict_room_creation_to_administrators"].RequiredBool( ),
            visible.Where( user => user.Administrator ).ToList( ),
            visible.Where( user => !user.Administrator ).ToList( ),
            name == "account_edit_paginated" ? "2" : null );

        var html = await Parity.Render( Slices.accounts.edit.Create( model ) );

        if ( viewContext.CanAdminister )
        {
            Parity.AssertAdminAccountEditDom( name, "html", html );
        }
        else
        {
            Parity.AssertDom( name, "html", html );
        }
    }

    [Fact]
    public async Task Users_page_turbo_stream_matches_rails( )
    {
        const string name = "account_users_page_2";
        var users = Case( name )["page_users"]!.AsArray( ).Select( user => Named( name, user.RequiredString( ) ) ).ToList( );

        var html = await Parity.Render( Slices.accounts.users.index_turbo_stream.Create( new AccountUsersTurboStreamPage( Context( name ), users, null ) ) );

        Parity.AssertDom( name, "turbo_stream.html", html );
    }

    [Fact]
    public async Task Bots_index_matches_rails( )
    {
        const string name = "bots_index";

        var html = await Parity.Render( Slices.accounts.bots.index.Create( new BotsIndexPage( Context( name ), Bots( name ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Fact]
    public async Task New_bot_matches_rails( )
    {
        const string name = "bots_new";

        var html = await Parity.Render( Slices.accounts.bots.new_html.Create( new BotsNewPage( Context( name ), new BotForm( ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Theory]
    [InlineData( "bots_edit" )]
    [InlineData( "bots_edit_with_avatar" )]
    public async Task Edit_bot_matches_rails( string name )
    {
        var bender = User( name, "Bender Bot" );
        var form = new BotForm( bender["name"].OptionalString( ), bender["webhook_url"].OptionalString( ), Case( name )["avatar_url"].OptionalString( ) );

        var html = await Parity.Render( Slices.accounts.bots.edit.Create( new BotsEditPage( Context( name ), bender["id"].RequiredLong( ), form ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Theory]
    [InlineData( "custom_styles_edit" )]
    [InlineData( "custom_styles_layout" )]
    public async Task Custom_styles_match_rails( string name )
    {
        var html = await Parity.Render( Slices.accounts.custom_styles.edit.Create( new CustomStylesEditPage(
            Context( name ),
            Account( name )["custom_styles"].OptionalString( ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    /// <summary><c>Account.first.users.active.bots.ordered</c> with each bot's shared rooms, by name.</summary>
    private static List<BotView> Bots( string name ) =>
        OrderedUsers( name ).Where( user => user is { IsBot: true, Active: true } ).Select( user =>
        {
            var botKey = User( name, user.Name )["bot_key"].RequiredString( );
            var rooms = Root["memberships"]!.AsArray( )
                .Where( membership => membership!["user_id"].RequiredLong( ) == user.Id )
                .Select( membership => RoomById( membership!["room_id"].RequiredLong( ) ) )
                .Where( room => room["type"].OptionalString( ) != "Rooms::Direct" )
                .Select( room => new BotRoomView( room["id"].RequiredLong( ), room["name"].RequiredString( ) ) )
                .OrderBy( room => room.Name.ToLowerInvariant( ), StringComparer.Ordinal )
                .ToList( );
            return new BotView( user, botKey, rooms );
        } ).ToList( );

    private static JsonNode RoomById( long id ) => Root["rooms"]!.AsObject( ).Select( pair => pair.Value! ).First( room => room["id"].RequiredLong( ) == id );
}
