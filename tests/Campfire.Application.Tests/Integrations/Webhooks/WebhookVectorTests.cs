using System.Text;
using System.Text.Json.Nodes;

using Campfire.Application.Integrations.Network;
using Campfire.Application.Integrations.Webhooks;
using Campfire.Application.Tests.Integrations.Support;

namespace Campfire.Application.Tests.Integrations.Webhooks;

/// <summary>
/// Replays <c>vectors/integrations/webhook_cases.json</c> against a local server and compares what
/// <see cref="WebhookDelivery"/> did with what <c>Webhook#deliver</c> did in the reference
/// (<c>webhook_expected.json</c>, from <c>oracle/webhook.rb</c>): the status, the reply (text bytes and validity, or
/// the attachment's name, type and bytes), the error class where Rails raised, and the request as sent.
/// </summary>
public sealed class WebhookVectorTests
{
    private const string Payload = "{\"message\":\"hi\"}";

    private static JsonArray Cases => JsonNode.Parse( IntegrationVectors.Text( "webhook_cases.json" ) )!.AsArray( );

    private static JsonArray Expected => JsonNode.Parse( IntegrationVectors.Text( "webhook_expected.json" ) )!.AsArray( );

    [Fact]
    public async Task Delivers_every_case_like_the_reference( )
    {
        var cases = Cases;
        var expected = Expected;
        Assert.Equal( 19, cases.Count );
        await using var server = FakeHttpServer.Start( cases.Select( testCase => Route( testCase! ) ) );
        using var delivery = new WebhookDelivery( IntegrationVectors.Network( SystemHostResolver.Instance, SystemSocketDialer.Instance ) );

        var outcomes = await Task.WhenAll( cases.Select( testCase => OutcomeAsync( delivery, server, testCase! ) ) );

        var failures = new List<string>( );
        for ( var index = 0; index < cases.Count; index++ )
        {
            var wanted = Wanted( expected[index]! );
            if ( wanted != outcomes[index] )
            {
                failures.Add( $"{cases[index]!["name"]}: expected {wanted}, got {outcomes[index]}" );
            }
        }

        Assert.True( failures.Count == 0, string.Join( '\n', failures ) );
    }

    /// <summary>
    /// The request carries <c>Net::HTTP</c>'s header names and values. <c>SocketsHttpHandler</c> writes content
    /// headers after the others, so <c>Content-Type</c> moves from first to just before <c>Content-Length</c>.
    /// </summary>
    [Fact]
    public async Task Sends_the_request_net_http_sends( )
    {
        var textCase = Cases[0]!;
        await using var server = FakeHttpServer.Start( Route( textCase ) );
        using var delivery = new WebhookDelivery( IntegrationVectors.Network( SystemHostResolver.Instance, SystemSocketDialer.Instance ) );

        await delivery.DeliverAsync( $"http://{server.Endpoint}/text", Payload, TestContext.Current.CancellationToken );

        var request = Assert.Single( server.Received );
        var sent = Expected[0]!["requests"]![0]!;
        Assert.Equal( "POST /text HTTP/1.1", sent["request_line"]!.GetValue<string>( ) );
        Assert.Equal( ("POST", "/text"), (request.Method, request.Target) );
        var headers = sent["headers"]!.AsArray( )
            .Select( header => new KeyValuePair<string, string>(
                header![0]!.GetValue<string>( ),
                header[0]!.GetValue<string>( ) == "Host" ? server.Endpoint.ToString( ) : header[1]!.GetValue<string>( ) ) )
            .ToList( );
        var contentType = headers[0];
        Assert.Equal( "Content-Type", contentType.Key );
        headers.RemoveAt( 0 );
        headers.Insert( headers.Count - 1, contentType );
        Assert.Equal( headers, request.Headers );
        Assert.Equal( sent["body"]!.GetValue<string>( ), Encoding.UTF8.GetString( request.Body ) );
    }

    private static FakeRoute Route( JsonNode definition )
    {
        var body = definition["body_b64"] is { } base64Body
            ? Convert.FromBase64String( base64Body.GetValue<string>( ) )
            : Encoding.UTF8.GetBytes( definition["body"]?.GetValue<string>( ) ?? "" );
        return new FakeRoute( "POST", "*", $"/{definition["name"]!.GetValue<string>( )}", definition["status"]!.GetValue<int>( ) )
        {
            Headers =
            [
                .. definition["headers"]!.AsArray( )
                    .Select( header => new KeyValuePair<string, string>( header![0]!.GetValue<string>( ), header[1]!.GetValue<string>( ) ) ),
            ],
            Body = body,
            Gzip = definition["gzip"]?.GetValue<bool>( ) ?? false,
            Delay = TimeSpan.FromSeconds( definition["delay"]?.GetValue<int>( ) ?? 0 ),
        };
    }

    private static async Task<string> OutcomeAsync( WebhookDelivery delivery, FakeHttpServer server, JsonNode testCase )
    {
        var url = testCase["url"]?.GetValue<string>( ) ?? $"http://{server.Endpoint}/{testCase["name"]!.GetValue<string>( )}";
        try
        {
            var outcome = await delivery.DeliverAsync( url, Payload, TestContext.Current.CancellationToken );
            return $"status={outcome.Status?.ToString( ) ?? "null"} reply={Describe( outcome.Reply )}";
        }
        catch ( WebhookDeliveryException error )
        {
            return $"error={error.RubyClass} reply=null";
        }
    }

    private static string Describe( WebhookReply? reply ) => reply switch
    {
        null => "null",
        WebhookTextReply text => $"text({Convert.ToBase64String( Encoding.UTF8.GetBytes( text.Text ) )},{text.IsValidUtf8})",
        WebhookAttachmentReply attachment => $"attachment({attachment.Filename},{attachment.ContentType},{Convert.ToBase64String( attachment.Data )})",
        _ => reply.ToString( ),
    };

    private static string Wanted( JsonNode expected )
    {
        var reply = expected["reply"] switch
        {
            null => "null",
            { } expectedReply when expectedReply["attachment"] is { } attachment =>
                $"attachment({attachment["filename"]},{attachment["content_type"]},{attachment["body_b64"]})",
            { } expectedReply => expectedReply["valid"]!.GetValue<bool>( )
                ? $"text({expectedReply["text_b64"]},True)"
                : $"text({Utf8RoundTrip( expectedReply["text_b64"]!.GetValue<string>( ) )},False)",
        };
        return expected["error"] is { } error
            ? $"error={error} reply={reply}"
            : $"status={expected["status"]?.ToString( ) ?? "null"} reply={reply}";
    }

    /// <summary>Base64 text decoded as UTF-8 and encoded again, as a lossy text reply carries it.</summary>
    private static string Utf8RoundTrip( string base64 ) =>
        Convert.ToBase64String( Encoding.UTF8.GetBytes( Encoding.UTF8.GetString( Convert.FromBase64String( base64 ) ) ) );
}
