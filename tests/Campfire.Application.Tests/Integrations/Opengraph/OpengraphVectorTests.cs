using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Integrations.Opengraph;
using Campfire.Application.Serialization;
using Campfire.Application.Tests.Integrations.Support;

namespace Campfire.Application.Tests.Integrations.Opengraph;

/// <summary>
/// Replays <c>vectors/integrations/opengraph_cases.json</c> (fake DNS, a local server for the fake public addresses)
/// and compares the response, the DNS lookups and the HTTP requests with what the reference did
/// (<c>opengraph_expected.json</c>, from <c>oracle/opengraph.rb</c>); and the controller's exact bytes against
/// <c>vectors/json/unfurl_link.json</c>.
/// </summary>
public sealed class OpengraphVectorTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unfurls_every_case_like_the_reference( )
    {
        var vectors = JsonNode.Parse( IntegrationVectors.Text( "opengraph_cases.json" ) )!;
        var expected = JsonNode.Parse( IntegrationVectors.Text( "opengraph_expected.json" ) )!.AsArray( );
        var cases = vectors["cases"]!.AsArray( );
        Assert.Equal( 90, cases.Count );
        await using var server = FakeHttpServer.Start( vectors["routes"]!.AsArray( ).Select( route => Route( route! ) ) );
        var fakePublic = vectors["public_ips"]!.AsArray( ).Select( ip => IPAddress.Parse( ip!.GetValue<string>( ) ) ).ToList( );

        var failures = new List<string>( );
        for ( var index = 0; index < cases.Count; index++ )
        {
            var name = cases[index]!["name"]!.GetValue<string>( );
            Assert.Equal( name, expected[index]!["name"]!.GetValue<string>( ) );
            var resolver = Resolver( vectors["hosts"]!.AsObject( ) );
            using var unfurler = new OpengraphUnfurler( IntegrationVectors.Network( resolver, new MappingDialer( fakePublic, server.Endpoint ) ) );
            var before = server.Received.Count;

            var response = await ResponseAsync( unfurler, cases[index]!["url"]!.GetValue<string>( ) );

            var requests = server.Received.Skip( before ).Select( request => new JsonArray(
                request.Method,
                request.Header( "Host" ),
                request.Target,
                request.Header( "Accept" ),
                request.Header( "Accept-Encoding" ),
                request.Header( "User-Agent" ) ) );
            var actual = new JsonObject
            {
                ["response"] = response,
                ["lookups"] = new JsonArray( [.. resolver.Lookups.Select( host => JsonValue.Create( host ) )] ),
                ["requests"] = new JsonArray( [.. requests] ),
            };
            var wanted = new JsonObject
            {
                ["response"] = expected[index]!["response"]!.DeepClone( ),
                ["lookups"] = expected[index]!["lookups"]!.DeepClone( ),
                ["requests"] = expected[index]!["requests"]!.DeepClone( ),
            };
            if ( wanted["response"]?["body"] is JsonValue expectedBody )
            {
                // Native JSON differs only in these Unicode escape spellings; retain the original vector.
                wanted["response"]!["body"] = NativeUnicodeEscapes( expectedBody.GetValue<string>( ) );
            }

            if ( !JsonNode.DeepEquals( actual, wanted ) )
            {
                failures.Add( $"{name}:\n  expected {wanted.ToJsonString( )}\n  actual   {actual.ToJsonString( )}" );
            }
        }

        Assert.True( failures.Count == 0, $"{failures.Count} of {cases.Count} cases differ:\n{string.Join( '\n', failures )}" );
    }

    /// <summary>
    /// <c>POST /unfurl_link</c> as the JSON generator ran it: a page whose tags carry the hostile string, a PNG image,
    /// and every host public. <c>render json:</c> leaves HTML unescaped and keeps the model's validation state.
    /// </summary>
    [Fact]
    public async Task Renders_the_json_the_controller_renders( )
    {
        const string hostile = "<b>Zoë</b> & \"Ünïcødé\" 'quotes' \u2028line\u2029para 😀 tab\tnewline\n bs\\ slash/ </script>";
        var page = "<html><head><meta charset=\"utf-8\">\n"
            + $"<meta property=\"og:title\" content=\"{Escape( hostile )}\">\n"
            + "<meta property=\"og:description\" content=\"Tags &lt;i&gt;stripped&lt;/i&gt; &amp; entities, \u2028 separators, naïve café 😀\">\n"
            + "<meta property=\"og:image\" content=\"https://unfurl.example/og.png\">\n"
            + "<meta property=\"og:url\" content=\"https://unfurl.example/canonical?a=1&amp;b=2\">\n"
            + "</head><body></body></html>\n";
        var certificate = IntegrationVectors.SelfSigned( "unfurl.example" );
        using var trusted = X509CertificateLoader.LoadCertificate( certificate.RawData );
        await using var server = FakeHttpServer.StartTls(
            certificate,
            new FakeRoute( "GET", "unfurl.example", "/page", 200 ).Header( "Content-Type", "text/html" ).WithBody( page ),
            new FakeRoute( "HEAD", "unfurl.example", "/og.png", 200 ).Header( "Content-Type", "image/png" ) );
        var resolver = new FakeHostResolver( ).Set( "unfurl.example", "93.184.216.34" );
        var network = IntegrationVectors.Network( resolver, new MappingDialer( [IPAddress.Parse( "93.184.216.34" )], server.Endpoint ) );
        using var unfurler = new OpengraphUnfurler( network with
        {
            TrustedRoots = [trusted]
        } );

        var result = await unfurler.UnfurlAsync( "https://unfurl.example/page", Cancellation );

        // Keep the imported Ruby vector byte-for-byte; the native JSON writer escapes line separators and non-BMP scalars.
        var expected = NativeUnicodeEscapes( RepositoryPaths.ReadVectorText( "json", "unfurl_link.json" ) );
        Assert.Equal( new UnfurlResult.Json( expected ), result );
    }

    private static string NativeUnicodeEscapes( string expected ) => expected
        .Replace( "\u2028", "\\u2028", StringComparison.Ordinal )
        .Replace( "\u2029", "\\u2029", StringComparison.Ordinal )
        .Replace( "😀", "\\uD83D\\uDE00", StringComparison.Ordinal );

    /// <summary><c>ERB::Util.html_escape</c>.</summary>
    private static string Escape( string value ) =>
        value.Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" ).Replace( "\"", "&quot;" ).Replace( "'", "&#39;" );

    private static async Task<JsonNode> ResponseAsync( OpengraphUnfurler unfurler, string url )
    {
        try
        {
            return await unfurler.UnfurlAsync( url, Cancellation ) switch
            {
                UnfurlResult.Json json => new JsonObject { ["status"] = 200, ["body"] = JsonPayload.EscapeHtmlEntities( json.Body ) },
                _ => new JsonObject { ["status"] = 204 },
            };
        }
        catch ( OpengraphUnfurlException error )
        {
            return new JsonObject { ["status"] = 500, ["error"] = error.RubyClass };
        }
    }

    private static FakeHostResolver Resolver( JsonObject hosts )
    {
        var resolver = new FakeHostResolver( );
        foreach ( var (host, answers) in hosts )
        {
            resolver.Set( host, [.. answers!.AsArray( ).Select( list => list!.AsArray( ).Select( ip => ip!.GetValue<string>( ) ).ToArray( ) )] );
        }

        return resolver;
    }

    private static FakeRoute Route( JsonNode definition )
    {
        byte[] body;
        if ( definition["body_b64"] is { } base64Body )
        {
            body = Convert.FromBase64String( base64Body.GetValue<string>( ) );
        }
        else if ( definition["body_repeat"] is JsonArray repeat )
        {
            body = Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( repeat[0]!.GetValue<string>( ), repeat[1]!.GetValue<int>( ) ) ) );
        }
        else
        {
            body = Encoding.UTF8.GetBytes( definition["body"]?.GetValue<string>( ) ?? "" );
        }

        if ( definition["pad_to"] is { } padTo && padTo.GetValue<int>( ) > body.Length )
        {
            body = [.. body, .. Enumerable.Repeat( ( byte ) ' ', padTo.GetValue<int>( ) - body.Length )];
        }

        return new FakeRoute(
            definition["method"]!.GetValue<string>( ),
            definition["host"]!.GetValue<string>( ),
            definition["path"]!.GetValue<string>( ),
            definition["status"]!.GetValue<int>( ) )
        {
            Headers =
            [
                .. definition["headers"]!.AsArray( )
                    .Select( header => new KeyValuePair<string, string>( header![0]!.GetValue<string>( ), header[1]!.GetValue<string>( ) ) ),
            ],
            Body = body,
            Chunked = definition["chunked"]?.GetValue<bool>( ) ?? false,
            Gzip = definition["gzip"]?.GetValue<bool>( ) ?? false,
        };
    }
}
