using System.Globalization;
using System.Text.Json.Nodes;
using Campfire.Web.Views;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Tests.Views.Support;

/// <summary>
/// A token-stream golden (views B: <c>vectors/views/b/&lt;name&gt;.json</c>, written by <c>reference-tools/views/b/run.sh</c>): the
/// reference request's context, the view-model input, and the canonical token stream Rails rendered (Rust
/// <c>crates/views/tests/messages_support/mod.rs</c> at <c>3fe9f61</c>).
/// </summary>
internal sealed class TokenStreamGolden
{
    private static readonly string[] RegionNames = ["title", "head", "nav", "main", "footer", "sidebar"];

    private TokenStreamGolden( string name, JsonNode json )
    {
        Name = name;
        Json = json;
        Kind = json["kind"].RequiredString( );
    }

    public string Name { get; }

    public string Kind { get; }

    public JsonNode Json { get; }

    public JsonNode Input => Json["input"]!;

    public static TokenStreamGolden Load( string name ) =>
        new( name, RepositoryPaths.ReadVectorJson( "views", "b", $"{name}.json" ) );

    /// <summary>The reference request's context.</summary>
    public ViewContext Context( LayoutKind layout = LayoutKind.Application )
    {
        var context = Json["context"]!;
        var assets = context["assets"]!.AsObject( ).ToDictionary( pair => pair.Key, pair => pair.Value.RequiredString( ), StringComparer.Ordinal );
        var user = context["current_user"] as JsonObject;
        var platform = context["platform"]!;
        bool Flag( string key ) => platform[key]?.GetValue<bool>( ) ?? false;
        return new ViewContext
        {
            CurrentUser = user is null
                ? null
                : new CurrentUserView(
                    user["id"].RequiredLong( ),
                    user["name"].RequiredString( ),
                    user["administrator"].RequiredBool( ),
                    user["bot"].RequiredBool( ),
                    user["avatar_url"].RequiredString( ) ),
            Account = new AccountSummary(
                context["account"]!["name"].RequiredString( ),
                context["account"]!["logo_url"].RequiredString( ),
                context["account"]!["has_logo"].RequiredBool( ) ),
            IncludeCsrfTags = true,
            Platform = new Platform
            {
                Ios = Flag( "ios" ),
                Android = Flag( "android" ),
                Mac = Flag( "mac" ),
                Windows = Flag( "windows" ),
                Chrome = Flag( "chrome" ),
                Firefox = Flag( "firefox" ),
                Safari = Flag( "safari" ),
                Edge = Flag( "edge" ),
                Mobile = Flag( "mobile" ),
                Desktop = Flag( "desktop" ),
                AppleMessages = Flag( "apple_messages" ),
                Browser = platform["browser"].OptionalString( ) ?? string.Empty,
                OperatingSystem = platform["operating_system"].OptionalString( ) ?? string.Empty,
            },

            // Marks where the layout ends and the page's head block starts (see HeadBlock).
            Assets = new AssetTagsView(
                logical => PageGoldenFacts.Required( assets.GetValueOrDefault( logical ), $"{Name}: unknown asset {logical}" ),
                SafeHtml.Empty,
                SafeHtml.Raw( "<script type=\"module\">import \"application\"</script>" ) ),
            BaseUrl = context["base_url"].RequiredString( ),
            LastRoomVisitedId = context["last_room_visited_id"] is JsonValue room ? room.GetValue<long>( ) : null,
            Layout = layout,
        };
    }

    /// <summary>DOM parity for a template rendered without a layout against a reference page: the page's main
    /// content (or a frame layout's body) against the whole render; fragments compare everything.</summary>
    public void AssertContent( string actualHtml )
    {
        var expected = Expected( );
        if ( Kind == "page" )
        {
            expected = RegionsNamed( expected, ["main"] ).FirstOrDefault( ).Tokens ?? [];
        }

        AssertSame( Name, CanonicalHtml.TrimWhitespace( expected ), CanonicalHtml.TrimWhitespace( CanonicalHtml.Tokens( actualHtml ) ) );
    }

    /// <summary>DOM parity: pages compare the regions the page fills (title, head block, nav, main, footer, sidebar).</summary>
    public void AssertDom( string actualHtml )
    {
        var expected = Expected( );
        var actual = CanonicalHtml.Tokens( actualHtml );
        if ( Kind != "page" )
        {
            AssertSame( Name, CanonicalHtml.TrimWhitespace( expected ), CanonicalHtml.TrimWhitespace( actual ) );
            return;
        }

        var regions = RegionsNamed( expected, RegionNames );
        Assert.NotEmpty( regions );
        var actualRegions = RegionsNamed( actual, regions.Select( region => region.Name ).ToArray( ) );
        foreach ( var ((name, expectedTokens), (_, actualTokens)) in regions.Zip( actualRegions ) )
        {
            AssertSame( $"{Name} [{name}]", expectedTokens, actualTokens );
        }
    }

    private List<string> Expected( ) =>
        Json["expected"]!.AsArray( ).Select( token => token.RequiredString( ).Replace(
            "name=\"csrf-param\" content=\"authenticity_token\"",
            "name=\"csrf-param\" content=\"\"",
            StringComparison.Ordinal ) ).ToList( );

    private static void AssertSame( string label, List<string> expected, List<string> actual )
    {
        if ( expected.SequenceEqual( actual ) )
        {
            return;
        }

        var index = 0;
        while ( index < expected.Count && index < actual.Count && expected[index] == actual[index] )
        {
            index++;
        }

        var from = Math.Max( 0, index - 4 );
        string Show( List<string> tokens ) => string.Join( "\n    ", tokens.Skip( from ).Take( index + 6 - from ) );
        var file = label.Replace( ' ', '_' ).Replace( '[', '_' ).Replace( ']', '_' );
        Parity.Save( $"{file}.expected", string.Join( '\n', expected ) );
        Parity.Save( $"{file}.actual", string.Join( '\n', actual ) );
        Assert.Fail( string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: DOM differs at token {index} (expected {expected.Count} tokens, got {actual.Count})\n" +
                $"  expected:\n    {Show( expected )}\n  actual:\n    {Show( actual )}" ) );
    }

    private static List<(string Name, List<string> Tokens)> RegionsNamed( List<string> tokens, string[] names )
    {
        if ( !tokens.Any( token => token.StartsWith( "<main id=\"main-content\"", StringComparison.Ordinal ) ) )
        {
            // A turbo-frame request's minimal layout: compare the body.
            return [("body", CanonicalHtml.TrimWhitespace( ElementChildren( tokens, token => token.StartsWith( "<body", StringComparison.Ordinal ) ) ))];
        }

        return names.Select( name => (name, CanonicalHtml.TrimWhitespace( Region( tokens, name ) )) ).ToList( );
    }

    private static List<string> Region( List<string> tokens, string name )
    {
        switch ( name )
        {
            case "title":
                return ElementChildren( tokens, token => token == "<title>" );
            case "head":
                return HeadBlock( tokens );
            case "nav":
                return ElementChildren( tokens, token => token.StartsWith( "<nav id=\"nav\"", StringComparison.Ordinal ) );
            case "main":
                var main = ElementChildren( tokens, token => token.StartsWith( "<main id=\"main-content\"", StringComparison.Ordinal ) );
                var footer = main.FindIndex( token => token.StartsWith( "<footer id=\"footer\"", StringComparison.Ordinal ) );
                return footer >= 0 ? main[..footer] : main;
            case "footer":
                return ElementChildren( tokens, token => token.StartsWith( "<footer id=\"footer\"", StringComparison.Ordinal ) );
            default:
                return ElementChildren( tokens, token => token.StartsWith( "<aside id=\"sidebar\"", StringComparison.Ordinal ) );
        }
    }

    /// <summary>The page's <c>yield :head</c>: everything in head after the importmap's module script.</summary>
    private static List<string> HeadBlock( List<string> tokens )
    {
        var head = ElementChildren( tokens, token => token == "<head>" );
        var script = head.FindLastIndex( token => token.StartsWith( "<script type=\"module\"", StringComparison.Ordinal ) );
        var start = script >= 0 ? script + 3 : head.Count;
        return head[Math.Min( start, head.Count )..];
    }

    private static List<string> ElementChildren( List<string> tokens, Func<string, bool> isStart )
    {
        var start = tokens.FindIndex( token => isStart( token ) );
        if ( start < 0 )
        {
            return [];
        }

        var name = TagName( tokens[start] );
        var depth = 0;
        for ( var offset = start + 1; offset < tokens.Count; offset++ )
        {
            var token = tokens[offset];
            if ( token.StartsWith( '<' ) && !token.StartsWith( "</", StringComparison.Ordinal ) && TagName( token ) == name )
            {
                depth++;
            }
            else if ( token == $"</{name}>" )
            {
                if ( depth == 0 )
                {
                    return tokens[(start + 1)..offset];
                }

                depth--;
            }
        }

        return tokens[(start + 1)..];
    }

    private static string TagName( string token ) => token.TrimStart( '<', '/' ).Split( [' ', '>'], 2 )[0];
}
