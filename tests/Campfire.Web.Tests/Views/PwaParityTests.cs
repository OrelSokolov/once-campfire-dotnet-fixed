using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Models;
using static Campfire.Web.Tests.Views.Support.PageGoldenFacts;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>Parity of the <c>pwa/*</c> views with the Rails goldens, per user agent (Rust <c>parity_a.rs</c>).</summary>
public sealed class PwaParityTests
{
    public static TheoryData<string> UserAgents => new( Root["user_agents"]!.AsObject( ).Select( pair => pair.Key ) );

    [Theory]
    [MemberData( nameof( UserAgents ) )]
    public async Task Browser_settings_match_rails( string userAgent )
    {
        var name = $"browser_settings_{userAgent}";

        var html = await Parity.Render( Slices.pwa._browser_settings.Create( new ContextPartial( Context( name ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Theory]
    [MemberData( nameof( UserAgents ) )]
    public async Task System_settings_match_rails( string userAgent )
    {
        var name = $"system_settings_{userAgent}";

        var html = await Parity.Render( Slices.pwa._system_settings.Create( new ContextPartial( Context( name ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Theory]
    [MemberData( nameof( UserAgents ) )]
    public async Task Install_instructions_match_rails( string userAgent )
    {
        var name = $"install_instructions_{userAgent}";

        var html = await Parity.Render( Slices.pwa._install_instructions.Create( new ContextPartial( Context( name ) ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Fact]
    public async Task Manifest_matches_rails_byte_for_byte( )
    {
        const string name = "manifest";
        var account = Account( name );
        var model = new ManifestPage(
            Context( name ),
            account["name"].OptionalString( ),
            account["logo_path_small"].RequiredString( ),
            account["logo_path"].RequiredString( ) );

        var json = await Parity.Render( Slices.pwa.manifest_json.Create( model ) );

        Assert.Equal( Golden( name, "json" ), json );
    }

    [Fact]
    public void Service_worker_matches_rails_byte_for_byte( )
    {
        var served = Slices.pwa.ServiceWorker.Script;

        Assert.Equal( Golden( "service_worker", "js" ), served );
    }
}
