using System.Text.Json.Nodes;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Models;
using static Campfire.Web.Tests.Views.Support.PageGoldenFacts;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>DOM parity of the <c>users/*</c> views with the Rails goldens (Rust <c>parity_a.rs</c>).</summary>
public sealed class UsersParityTests
{
    [Theory]
    [InlineData( "users_show_self", "David" )]
    [InlineData( "users_show_member_as_admin", "JZ" )]
    [InlineData( "users_show_member_as_member", "JZ" )]
    [InlineData( "users_show_bot", "Bender Bot" )]
    [InlineData( "users_show_deactivated", "Ex Employee" )]
    [InlineData( "users_show_banned", "Spam Ham" )]
    public async Task Show_matches_rails( string name, string shown )
    {
        var model = new UsersShowPage( Context( name ), Named( name, shown ), User( name, shown )["transfer_id"].RequiredString( ) );

        var html = await Parity.Render( Slices.users.show.Create( model ) );

        Parity.AssertDom( name, "html", html );
    }

    [Theory]
    [InlineData( "profile_chrome_mac" )]
    [InlineData( "profile_chrome_windows" )]
    [InlineData( "profile_safari_mac" )]
    [InlineData( "profile_safari_ios" )]
    [InlineData( "profile_chrome_android" )]
    [InlineData( "profile_firefox_mac" )]
    [InlineData( "profile_firefox_android" )]
    [InlineData( "profile_edge_windows" )]
    [InlineData( "profile_kevin" )]
    [InlineData( "profile_with_avatar" )]
    public async Task Profile_matches_rails( string name )
    {
        var me = UserByEmail( name, Case( name )["as"].RequiredString( ) );
        var profile = Data( name )["profile"]!;
        var model = new ProfilePage(
            Context( name ),
            Summary( me ),
            me["avatar_attached"].RequiredBool( ),
            me["transfer_id"].RequiredString( ),
            Memberships( profile["shared"] ),
            Memberships( profile["direct"] ) );

        var html = await Parity.Render( Slices.users.profiles.show.Create( model ) );

        Parity.AssertDom( name, "html", html );
    }

    [Fact]
    public async Task Push_subscriptions_match_rails( )
    {
        const string name = "push_subscriptions";
        var subscriptions = Data( name )["push_subscriptions"]!.AsArray( )
            .Select( subscription => new PushSubscriptionView(
                subscription!["id"].RequiredLong( ),
                subscription["endpoint"].RequiredString( ),
                subscription["browser"].RequiredString( ),
                subscription["version"].RequiredString( ),
                subscription["platform"].RequiredString( ) ) )
            .ToList( );

        var html = await Parity.Render( Slices.users.push_subscriptions.index.Create( new PushSubscriptionsPage( Context( name ), subscriptions ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Fact]
    public async Task New_matches_rails( )
    {
        const string name = "users_new";
        var model = new UsersNewPage( Context( name ), Account( name )["join_code"].RequiredString( ), HelpContact( name ) );

        var html = await Parity.Render( Slices.users.new_html.Create( model ) );

        Parity.AssertDom( name, "html", html );
    }

    [Theory]
    [InlineData( "avatar_david", "David" )]
    [InlineData( "avatar_three_initials", "Anna Bea Cole" )]
    public async Task Avatar_svg_matches_rails_byte_for_byte( string name, string shown )
    {
        var user = User( name, shown );

        var svg = await Parity.Render( Slices.users.avatars.show_svg.Create( new AvatarSvg(
            user["id"].RequiredLong( ),
            user["initials"].RequiredString( ) ) ) );

        Parity.AssertDom( name, "svg", svg );
        Assert.Equal( Golden( name, "svg" ), svg );
    }

    [Fact]
    public async Task Mention_matches_rails( )
    {
        const string name = "mention";

        var html = await Parity.Render( Slices.users._mention.Create( new MentionPartial( Context( name ), Mention( name, "JZ" ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Fact]
    public async Task Ban_button_rendered_outside_a_request_matches_rails( )
    {
        const string name = "ban_button_banned";

        var html = await Parity.Render( Slices.users._ban_button.Create( new BanButtonPartial(
            Context( name, new PageGoldenRequest( Partial: true ) ),
            Named( name, "Spam Ham" ) ) ) );

        Parity.AssertDom( name, "html", html );
        Assert.Equal( Golden( name, "html" ), html );
    }

    [Fact]
    public async Task Autocompletable_template_matches_rails( )
    {
        const string name = "autocompletables_template";

        var html = await Parity.Render( Slices.users.autocompletables._template.Create( new ContextPartial( Context( name ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    private static List<ProfileMembership> Memberships( JsonNode? list ) =>
        list!.AsArray( ).Select( membership => new ProfileMembership(
            membership!["room_id"].RequiredLong( ),
            RoomKindOf( membership["param_key"].RequiredString( ) ),
            membership["display_name"].RequiredString( ),
            membership["involvement"].RequiredString( ) ) ).ToList( );

    private static RoomKind RoomKindOf( string paramKey ) => Enum.GetValues<RoomKind>( ).Single( kind => kind.ParamKey( ) == paramKey );
}
