using System.IO.Compression;
using System.Net;

using Campfire.Application.Integrations.Network;
using Campfire.Application.Integrations.Webhooks;
using Campfire.Application.Tests.Integrations.Support;

namespace Campfire.Application.Tests.Integrations.Webhooks;

/// <summary>
/// <c>Webhook#deliver</c>'s policy: unrestricted destinations, the 7-second timeouts and their reply, and the bounds
/// this port adds (a 60-second delivery deadline and a 100 MiB reply, see docs/compatibility.md#deliberate-differences-and-bounds).
/// </summary>
public sealed class WebhookDeliveryTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static OutboundNetwork LocalNetwork => IntegrationVectors.Network( SystemHostResolver.Instance, SystemSocketDialer.Instance );

    /// <summary>Unguarded: private and loopback addresses are fine, names resolve normally, and nothing is pinned.</summary>
    [Fact]
    public async Task Reaches_internal_services( )
    {
        await using var server = FakeHttpServer.Start( new FakeRoute( "POST", "*", "/hook", 200 ).Header( "Content-Type", "text/plain" ).WithBody( "ok" ) );
        var resolver = new FakeHostResolver( ).Set( "bots.internal", "10.0.0.7" );
        var dialer = new MappingDialer( [IPAddress.Parse( "10.0.0.7" )], server.Endpoint );
        using var delivery = new WebhookDelivery( IntegrationVectors.Network( resolver, dialer ) );

        var outcome = await delivery.DeliverAsync( "http://bots.internal:8080/hook", "{}", Cancellation );

        Assert.Equal( new WebhookOutcome( 200, new WebhookTextReply( "ok", true ) ), outcome );
        Assert.Equal( ["bots.internal"], resolver.Lookups );
        Assert.Equal( ["10.0.0.7:8080"], dialer.Dialed );
        Assert.Equal( "bots.internal:8080", server.Received[0].Header( "Host" ) );
    }

    [Fact]
    public async Task Answers_a_connect_timeout_with_a_text_reply( )
    {
        using var delivery = new WebhookDelivery(
            IntegrationVectors.Network( SystemHostResolver.Instance, new HangingDialer( ) ),
            TimeSpan.FromSeconds( 1 ),
            TimeSpan.FromSeconds( 30 ),
            1024 );

        var outcome = await delivery.DeliverAsync( "http://127.0.0.1:1/hook", "{}", Cancellation );

        Assert.Equal( new WebhookOutcome( null, new WebhookTextReply( "Failed to respond within 1 seconds", true ) ), outcome );
    }

    /// <summary>An endpoint that keeps sending never trips the read timeout, but the delivery as a whole gives up and says so.</summary>
    [Fact]
    public async Task Gives_up_on_a_trickling_reply_at_the_deadline( )
    {
        await using var server = SlowServer.Trickling( "HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nConnection: close\r\n\r\n" );
        using var delivery = new WebhookDelivery( LocalNetwork, TimeSpan.FromSeconds( 7 ), TimeSpan.FromSeconds( 1 ), WebhookDelivery.MaxReplySize );
        var started = TimeProvider.System.GetTimestamp( );

        var outcome = await delivery.DeliverAsync( $"http://{server.Endpoint}/hook", "{}", Cancellation );

        Assert.Equal( new WebhookOutcome( null, new WebhookTextReply( "Failed to respond within 1 seconds", true ) ), outcome );
        Assert.True( TimeProvider.System.GetElapsedTime( started ) < TimeSpan.FromSeconds( 5 ) );
    }

    [Fact]
    public async Task A_silent_endpoint_times_out_on_the_read( )
    {
        await using var server = SlowServer.Silent( );
        using var delivery = new WebhookDelivery( LocalNetwork, TimeSpan.FromMilliseconds( 300 ), TimeSpan.FromSeconds( 30 ), 1024 );

        var outcome = await delivery.DeliverAsync( $"http://{server.Endpoint}/hook", "{}", Cancellation );

        Assert.Null( outcome.Status );
        Assert.IsType<WebhookTextReply>( outcome.Reply );
    }

    /// <summary>A reply that inflates past the limit fails the delivery, having inflated little more than the limit.</summary>
    [Fact]
    public async Task Refuses_a_reply_over_the_limit_even_when_compressed( )
    {
        var bomb = GzipOfZeros( 64 * 1024 * 1024 );
        await using var server = FakeHttpServer.Start( new FakeRoute( "POST", "*", "/hook", 200 ) { Body = bomb }
            .Header( "Content-Type", "image/png" ).Header( "Content-Encoding", "gzip" ) );
        using var delivery = new WebhookDelivery( LocalNetwork, TimeSpan.FromSeconds( 7 ), TimeSpan.FromSeconds( 30 ), 1024 * 1024 );

        var error = await Assert.ThrowsAsync<WebhookDeliveryException>( ( ) => delivery.DeliverAsync( $"http://{server.Endpoint}/hook", "{}", Cancellation ) );

        Assert.Equal( "Campfire::ReplyTooLarge", error.RubyClass );
        Assert.True( bomb.Length < 1024 * 1024 );
    }

    [Theory]
    [InlineData( null, "ArgumentError" )]
    [InlineData( "not a url", "URI::InvalidURIError" )]
    [InlineData( "ftp://example.com/hook", "ArgumentError" )]
    [InlineData( "mailto:foo", "URI::InvalidComponentError" )]
    [InlineData( "http:///hook", "ArgumentError" )]
    [InlineData( "http://nowhere.invalid/hook", "SocketError" )]
    public async Task Raises_where_rails_raises( string? url, string rubyClass )
    {
        using var delivery = new WebhookDelivery( IntegrationVectors.Network( new FakeHostResolver( ), SystemSocketDialer.Instance ) );

        var error = await Assert.ThrowsAsync<WebhookDeliveryException>( ( ) => delivery.DeliverAsync( url, "{}", Cancellation ) );

        Assert.Equal( rubyClass, error.RubyClass );
    }

    [Fact]
    public void Looks_up_mime_types_like_rails( )
    {
        Assert.Equal( ("jpeg", "image/jpeg"), MimeLookup.Lookup( "image/jpeg" ) );
        Assert.Equal( ("gzip", "application/gzip"), MimeLookup.Lookup( "application/x-gzip" ) );
        Assert.Equal( (null, "IMAGE/PNG"), MimeLookup.Lookup( "IMAGE/PNG" ) );
        Assert.Equal( ("html", "text/html"), MimeLookup.Lookup( "text/html; charset=utf-8" ) );
        Assert.Equal( (null, "video/quicktime"), MimeLookup.Lookup( "video/quicktime" ) );
        Assert.Equal( (null, "a/b"), MimeLookup.Lookup( "a/b; x=\"y z\"" ) );
        foreach ( var invalid in new[] { "image", "", "text/html, text", "a/b c", new string( 'a', 200 ) + "/b" } )
        {
            Assert.Equal( "Mime::Type::InvalidMimeType", Assert.Throws<WebhookDeliveryException>( ( ) => MimeLookup.Lookup( invalid ) ).RubyClass );
        }
    }

    private static byte[] GzipOfZeros( int size )
    {
        using var output = new MemoryStream( );
        using ( var gzip = new GZipStream( output, CompressionLevel.SmallestSize ) )
        {
            gzip.Write( new byte[size] );
        }

        return output.ToArray( );
    }
}
