using System.Globalization;
using System.Text.Json.Nodes;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Json;
using static Campfire.Web.Tests.Views.Support.PageGoldenFacts;

namespace Campfire.Web.Tests.Views;

/// <summary>The Jbuilder views, byte for byte against the Rails goldens (Rust <c>parity_a.rs</c> and <c>messages_views.rs</c>).</summary>
public sealed class JsonViewsTests
{
    private static readonly DateTimeOffset Then = DateTimeOffset.Parse( "2026-09-26T13:03:59.852991Z", CultureInfo.InvariantCulture );

    [Fact]
    public void User_json_matches_rails( )
    {
        const string name = "user_json";

        var json = JbuilderViews.User( JsonEntry.Of( ToUserJson( User( name, "JZ" ) ) ) );

        Assert.Equal( Golden( name, "json" ), json );
    }

    [Fact]
    public void Autocompletable_users_json_matches_rails( )
    {
        const string name = "autocompletable_users_json";
        var users = Data( name )["autocompletable"]!.AsArray( ).Select( userName =>
        {
            var user = User( name, userName.RequiredString( ) );
            return AutocompletableUserJson.For(
                user["name"].RequiredString( ),
                user["id"].RequiredLong( ),
                BaseUrl + user["avatar_path"].OptionalString( ),
                user["attachable_sgid"].RequiredString( ) );
        } );

        var json = JbuilderViews.AutocompletableUsersIndex( users );

        Assert.Equal( Golden( name, "json" ), json );
    }

    [Fact]
    public void Bot_messages_index_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_by_bots_index" );

        var json = JbuilderViews.MessagesByBotsIndex( golden.Input.AsArray( ).Select( message => JsonEntry.Of( Message( message! ) ) ) );

        Assert.Equal( golden.Json["raw"].OptionalString( ), json );
    }

    [Fact]
    public void Bot_message_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_by_bots_show" );

        var json = JbuilderViews.MessagesByBotsShow( JsonEntry.Of( Message( golden.Input ) ) );

        Assert.Equal( golden.Json["raw"].OptionalString( ), json );
    }

    [Fact]
    public void Bot_boost_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_boosts_by_bots_show" );

        var json = JbuilderViews.BoostsByBotsShow( JsonEntry.Of( Boost( golden.Input ) ) );

        Assert.Equal( NativeJsonEscapes( golden.Json["raw"].RequiredString( ) ), json );
    }

    [Fact]
    public void Cached_fragments_render_the_same_bytes_and_skip_building( )
    {
        var golden = TokenStreamGolden.Load( "messages_by_bots_index" );
        var cache = new FragmentCache( );
        var builds = 0;
        var entries = golden.Input.AsArray( ).Select( node =>
        {
            var message = Message( node! );
            var key = FragmentKeys.MessageJson( new( message.Id, Then ), new( message.Creator.Id, Then ), "http://campfire.test" );
            return new JsonEntry<MessageJson>( key, ( ) =>
            {
                builds++;
                return message;
            } );
        } ).ToList( );

        var first = JbuilderViews.MessagesByBotsIndex( entries, cache );
        var second = JbuilderViews.MessagesByBotsIndex( entries, cache );

        Assert.Equal( golden.Json["raw"].OptionalString( ), first );
        Assert.Equal( first, second );
        Assert.Equal( entries.Count, builds );
        Assert.Equal( entries.Count, cache.Count );
    }

    [Fact]
    public void Fragments_are_stored_before_the_html_entity_pass( )
    {
        var golden = TokenStreamGolden.Load( "messages_boosts_by_bots_show" );
        var boost = Boost( golden.Input );
        var cache = new FragmentCache( );
        var key = FragmentKeys.BoostJson( new( boost.Id, Then ), new( boost.Booster.Id, Then ), "http://campfire.test" );

        var json = JbuilderViews.BoostsByBotsShow( new JsonEntry<BoostJson>( key, ( ) => boost ), cache );

        Assert.Equal( NativeJsonEscapes( golden.Json["raw"].RequiredString( ) ), json );
        Assert.True( cache.TryGetValue<string>( key, out var fragment ) );
        Assert.Contains( "\"\\uD83E\\uDD16 <&>\"", fragment, StringComparison.Ordinal );
    }

    [Fact]
    public void Json_keys_change_with_their_inputs( )
    {
        string[] keys =
        [
            FragmentKeys.MessageJson( new( 1, Then ), new( 2, Then ), "http://a" ),
            FragmentKeys.MessageJson( new( 1, Then.AddTicks( 10 ) ), new( 2, Then ), "http://a" ),
            FragmentKeys.MessageJson( new( 1, Then ), new( 2, Then.AddSeconds( 1 ) ), "http://a" ),
            FragmentKeys.MessageJson( new( 1, Then ), new( 2, Then ), "http://b" ),
            FragmentKeys.BoostJson( new( 1, Then ), new( 2, Then ), "http://a" ),
            FragmentKeys.UserJson( new( 1, Then ), "http://a" ),
            FragmentKeys.UserJson( new( 1, Then ), "http://b" ),
        ];

        Assert.Equal( keys.Length, keys.Distinct( StringComparer.Ordinal ).Count( ) );
        Assert.StartsWith( "jbuilder/views/messages/_message/messages/1-20260926130359852991/", keys[0], StringComparison.Ordinal );
    }

    private static UserJson ToUserJson( JsonNode user ) =>
        new( user["id"].RequiredLong( ), user["name"].RequiredString( ), user["role"].RequiredString( ), BaseUrl + user["avatar_path"].OptionalString( ) );

    private static UserJson BotUser( JsonNode node ) =>
        new( node["id"].RequiredLong( ), node["name"].RequiredString( ), node["role"].RequiredString( ), node["avatar_url"].RequiredString( ) );

    private static MessageJson Message( JsonNode node ) => new(
        node["id"].RequiredLong( ),
        TokenStreamGoldenInputs.Time( node["created_at"] ),
        node["body"]!["plain_text"].RequiredString( ),
        node["body"]!["html"].RequiredString( ),
        BotUser( node["creator"]! ),
        node["room"]!["id"].RequiredLong( ),
        node["url"].RequiredString( ) );

    private static BoostJson Boost( JsonNode node ) => new(
        node["id"].RequiredLong( ),
        node["content"].RequiredString( ),
        TokenStreamGoldenInputs.Time( node["created_at"] ),
        BotUser( node["booster"]! ),
        node["message"]!["id"].RequiredLong( ),
        node["message"]!["url"].RequiredString( ) );

    // The immutable Rails vector keeps this non-BMP value raw; the native UTF-8 JSON writer emits its UTF-16 surrogate pair.
    private static string NativeJsonEscapes( string json ) => json
        .Replace( "🤖", "\\uD83E\\uDD16", StringComparison.Ordinal )
        .Replace( "\u2028", "\\u2028", StringComparison.Ordinal )
        .Replace( "\u2029", "\\u2029", StringComparison.Ordinal );
}
