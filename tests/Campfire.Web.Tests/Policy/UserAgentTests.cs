using System.Text.Json.Nodes;
using Campfire.Web.Policy;

namespace Campfire.Web.Tests.Policy;

/// <summary>
/// The useragent gem port and <c>ApplicationPlatform</c> against
/// <c>vectors/campfire_user_agents.json</c> (Rails-generated), ported from Rust
/// <c>crates/campfire/src/concerns/user_agent.rs</c> and <c>platform.rs</c> tests at <c>3fe9f61</c>.
/// </summary>
public sealed class UserAgentTests
{
    private static readonly JsonNode Vector = RepositoryPaths.ReadVectorJson( "campfire_user_agents.json" );

    public static TheoryData<int> AgentIndexes( ) => [.. Enumerable.Range( 0, Vector["user_agents"]!.AsArray( ).Count )];

    [Theory]
    [MemberData( nameof( AgentIndexes ) )]
    public void The_gem_answers_match( int index )
    {
        var testCase = Vector["user_agents"]![index]!;
        var agent = UserAgent.Parse( testCase["ua"]?.GetValue<string>( ) ?? "" );
        var failures = new List<string>( );

        Check( failures, "browser", testCase["browser"], ( ) => JsonValue.Create( agent.BrowserOrRaise( ) ) );
        Check( failures, "version", testCase["version"], ( ) => JsonValue.Create( agent.VersionOrRaise( )?.Text ) );
        Check( failures, "platform", testCase["platform"], ( ) => JsonValue.Create( agent.PlatformOrRaise( ) ) );
        Check( failures, "os", testCase["os"], ( ) => JsonValue.Create( agent.OperatingSystemOrRaise( ) ) );
        Check( failures, "bot", testCase["bot"], ( ) => JsonValue.Create( agent.IsBot ) );
        Check( failures, "mobile", testCase["mobile"], ( ) => JsonValue.Create( agent.MobileOrRaise( ) ) );

        Assert.True( failures.Count == 0, $"{testCase["ua"]}: {string.Join( "; ", failures )}" );
    }

    [Theory]
    [MemberData( nameof( AgentIndexes ) )]
    public void Application_platform_attributes_match( int index )
    {
        var testCase = Vector["user_agents"]![index]!;
        var userAgent = testCase["ua"]?.GetValue<string>( );
        var platform = new ApplicationPlatform( userAgent );
        var expected = testCase["application_platform"]!;
        var failures = new List<string>( );

        Check( failures, "ios", expected["ios"], ( ) => JsonValue.Create( platform.IsIos ) );
        Check( failures, "android", expected["android"], ( ) => JsonValue.Create( platform.IsAndroid ) );
        Check( failures, "mac", expected["mac"], ( ) => JsonValue.Create( platform.IsMac ) );
        Check( failures, "chrome", expected["chrome"], ( ) => JsonValue.Create( platform.ChromeOrRaise( ) ) );
        Check( failures, "firefox", expected["firefox"], ( ) => JsonValue.Create( platform.FirefoxOrRaise( ) ) );
        Check( failures, "safari", expected["safari"], ( ) => JsonValue.Create( platform.SafariOrRaise( ) ) );
        Check( failures, "edge", expected["edge"], ( ) => JsonValue.Create( platform.EdgeOrRaise( ) ) );
        Check( failures, "apple_messages", expected["apple_messages"], ( ) => JsonValue.Create( platform.IsAppleMessages ) );
        Check( failures, "mobile", expected["mobile"], ( ) => JsonValue.Create( platform.IsMobile ) );
        Check( failures, "desktop", expected["desktop"], ( ) => JsonValue.Create( platform.IsDesktop ) );
        Check( failures, "windows", expected["windows"], ( ) => JsonValue.Create( platform.WindowsOrRaise( ) ) );
        Check( failures, "operating_system", expected["operating_system"], ( ) => JsonValue.Create( platform.OperatingSystemOrRaise( ) ) );
        Check( failures, "browser", expected["browser"], ( ) => JsonValue.Create( platform.UserAgent.BrowserOrRaise( ) ) );
        Assert.True( failures.Count == 0, $"{userAgent}: {string.Join( "; ", failures )}" );
    }

    [Fact]
    public void Versions_split_like_the_gem( )
    {
        var failures = new List<string>( );
        foreach ( var testCase in Vector["versions"]!.AsArray( ) )
        {
            var version = new UserAgentVersion( testCase!["string"]!.GetValue<string>( ) );
            var expected = testCase["to_a"]!.AsArray( ).Select( segment => segment!.GetValue<string>( ) );
            if ( version.IsNil != testCase["nil"]!.GetValue<bool>( ) || !expected.SequenceEqual( version.Segments.Select( segment => segment.ToString( ) ) ) )
            {
                failures.Add( $"{testCase.ToJsonString( )}: nil={version.IsNil} to_a={string.Join( ",", version.Segments )}" );
            }
        }

        Assert.True( failures.Count == 0, string.Join( "\n", failures ) );
    }

    [Fact]
    public void Versions_compare_like_the_gem( )
    {
        var failures = new List<string>( );
        foreach ( var testCase in Vector["comparisons"]!.AsArray( ) )
        {
            var left = new UserAgentVersion( testCase!["a"]!.GetValue<string>( ) );
            var right = new UserAgentVersion( testCase["b"]!.GetValue<string>( ) );
            var comparison = UserAgentVersion.Compare( left, right );
            if ( comparison != testCase["cmp"]!.GetValue<int>( )
                || (left < right) != testCase["lt"]!.GetValue<bool>( )
                || (left == right) != testCase["eq"]!.GetValue<bool>( ) )
            {
                failures.Add( $"{testCase.ToJsonString( )}: cmp={comparison} lt={left < right} eq={left == right}" );
            }
        }

        Assert.True( failures.Count == 0, string.Join( "\n", failures ) );
    }

    [Fact]
    public void Blank_user_agents_parse_as_the_default( )
    {
        var agent = UserAgent.Parse( "  " );

        Assert.Equal( "Mozilla", agent.Browser );
        Assert.Equal( "4.0", agent.Version.Text );
    }

    /// <summary>Compares a Ruby value (<c>{"error": ...}</c> when it raised) with ours.</summary>
    private static void Check( List<string> failures, string field, JsonNode? expected, Func<JsonNode?> actual )
    {
        JsonNode? value;
        try
        {
            value = actual( );
        }
        catch ( UserAgentRaisedException )
        {
            if ( expected?["error"] is null )
            {
                failures.Add( $"{field}: expected {expected?.ToJsonString( ) ?? "null"}, raised" );
            }

            return;
        }

        if ( expected is JsonObject error && error.ContainsKey( "error" ) )
        {
            failures.Add( $"{field}: expected a raise, got {value?.ToJsonString( ) ?? "null"}" );
        }
        else if ( !JsonNode.DeepEquals( expected, value ) )
        {
            failures.Add( $"{field}: expected {expected?.ToJsonString( ) ?? "null"}, got {value?.ToJsonString( ) ?? "null"}" );
        }
    }
}
