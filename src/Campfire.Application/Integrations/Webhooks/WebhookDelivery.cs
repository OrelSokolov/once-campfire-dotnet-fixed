using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.Unicode;

using Campfire.Application.Bots;
using Campfire.Application.Integrations.Network;
using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Webhooks;

/// <summary>
/// <c>Webhook#deliver</c> (<c>app/models/webhook.rb</c>): the message goes to the bot's webhook as JSON, and the reply
/// becomes the bot's answer: text for a 200 <c>text/html</c> or <c>text/plain</c>, otherwise an attachment of any type
/// <c>Mime::Type</c> accepts.
/// </summary>
/// <remarks>
/// Intentionally unrestricted, unlike Opengraph and Web Push: only an administrator sets the URL, and it may point at
/// internal services, so the client resolves normally and honors the environment's proxy as <c>Net::HTTP</c> does.
/// Connecting and each read time out after <see cref="Webhook.EndpointTimeout"/> (7 s), and a timeout is itself answered
/// with a text reply. Bounds this port adds (documented in docs/compatibility.md#deliberate-differences-and-bounds;
/// from the Rust port's <c>edd0017</c>): the whole delivery has <see cref="DeliveryDeadline"/> (60 s), answered like a
/// timeout, since the per-read timeout restarts on every byte; and a reply over <see cref="MaxReplySize"/> (100 MiB
/// after decompression) fails the delivery.
/// </remarks>
public sealed class WebhookDelivery : IDisposable
{
    /// <summary>The most one delivery may take, connecting and reading the reply included.</summary>
    public static readonly TimeSpan DeliveryDeadline = TimeSpan.FromSeconds( 60 );

    /// <summary>The largest reply read, after decompression; Rails reads any size into memory.</summary>
    public const long MaxReplySize = 100L * 1024 * 1024;

    private readonly HttpMessageInvoker _http;

    private readonly TimeSpan _endpointTimeout;

    private readonly TimeSpan _deadline;

    private readonly long _maxReplySize;

    public WebhookDelivery( OutboundNetwork network ) : this( network, Webhook.EndpointTimeout, DeliveryDeadline, MaxReplySize )
    {
    }

    internal WebhookDelivery( OutboundNetwork network, TimeSpan endpointTimeout, TimeSpan deadline, long maxReplySize )
    {
        _http = OutboundHttpClients.CreateResolving( network, endpointTimeout, endpointTimeout );
        _endpointTimeout = endpointTimeout;
        _deadline = deadline;
        _maxReplySize = maxReplySize;
    }

    /// <summary>
    /// <c>deliver(message)</c> with the payload already built (<see cref="WebhookPayload"/>): posts it to
    /// <paramref name="url"/> and returns the reply to post. Throws <see cref="WebhookDeliveryException"/> where Rails raises.
    /// </summary>
    public async Task<WebhookOutcome> DeliverAsync( string? url, string payload, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( payload );

        var endpoint = Endpoint( url, out var requestUri );
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        deadline.CancelAfter( _deadline );
        try
        {
            var (status, contentType, body) = await PostAsync( endpoint, requestUri, payload, deadline.Token );
            return new WebhookOutcome( status, Reply( status, contentType, body ) );
        }
        catch ( NetHttpException error ) when ( error.IsTimeout )
        {
            return TimedOut( _endpointTimeout );
        }
        catch ( NetHttpException error )
        {
            throw new WebhookDeliveryException( error.RubyClass, error.Message, error );
        }
        catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
        {
            return TimedOut( _deadline );
        }
    }

    public void Dispose( ) => _http.Dispose( );

    /// <summary><c>"Failed to respond within #{ENDPOINT_TIMEOUT} seconds"</c>, as a text reply.</summary>
    private static WebhookOutcome TimedOut( TimeSpan after ) =>
        new( null, new WebhookTextReply( $"Failed to respond within {( int ) after.TotalSeconds} seconds", IsValidUtf8: true ) );

    /// <summary>
    /// <c>URI(url)</c>, <c>Net::HTTP.new(uri.host, uri.port)</c> with <c>use_ssl = (uri.scheme == "https")</c>, and
    /// <c>Net::HTTP::Post.new(uri, ...)</c>'s checks.
    /// </summary>
    private static NetHttpEndpoint Endpoint( string? url, out string requestUri )
    {
        if ( url is null )
        {
            throw new WebhookDeliveryException( RubyErrorClasses.ArgumentError, "bad argument (expected URI object or URI string)" );
        }

        switch ( RubyUri.TryParse( url, out var uri ) )
        {
            case RubyUriError.InvalidUri:
                throw new WebhookDeliveryException( RubyErrorClasses.InvalidUri, RubyUri.InvalidUriMessage( url ) );
            case RubyUriError.InvalidComponent:
                throw new WebhookDeliveryException( RubyErrorClasses.InvalidUriComponent, $"bad component: \"{url}\"" );
        }

        if ( !uri!.IsHttp )
        {
            throw new WebhookDeliveryException( RubyErrorClasses.ArgumentError, "not an HTTP URI" );
        }

        if ( string.IsNullOrEmpty( uri.Host ) || uri.Host == "[]" )
        {
            throw new WebhookDeliveryException( RubyErrorClasses.ArgumentError, "no host component for URI" );
        }

        if ( !uri.TryGetTcpPort( out var port ) )
        {
            throw new WebhookDeliveryException( RubyErrorClasses.SocketError, $"getaddrinfo: Servname not supported for ai_socktype ({uri.Port})" );
        }

        requestUri = NetHttpRequest.RequestUri( uri );
        return new NetHttpEndpoint( uri.IsHttps, uri.Host, port );
    }

    /// <summary><c>post(payload)</c>: the status, content type and body.</summary>
    private async Task<(int Status, string? ContentType, byte[] Body)> PostAsync(
        NetHttpEndpoint endpoint, string requestUri, string payload, CancellationToken cancellationToken )
    {
        using var request = NetHttpRequest.Create(
            HttpMethod.Post,
            endpoint,
            requestUri,
            HostHeaderPlacement.AfterUserAgent,
            connectionClose: true,
            headers: [new( HttpHeaderNames.ContentType, MediaTypeNames.Application.Json )],
            body: Encoding.UTF8.GetBytes( payload ) );
        using var response = await NetHttpRequest.SendAsync( _http, request, cancellationToken );
        var body = await response.ReadBodyAsync( _maxReplySize, cancellationToken );
        if ( body is null )
        {
            throw new WebhookDeliveryException(
                "Campfire::ReplyTooLarge", $"the reply is larger than {(_maxReplySize / 1024 / 1024).ToString( CultureInfo.InvariantCulture )} MB" );
        }

        return (response.Status, response.ContentType, body);
    }

    /// <summary><c>extract_text_from</c>, else <c>extract_attachment_from</c>.</summary>
    private static WebhookReply? Reply( int status, string? contentType, byte[] body )
    {
        if ( contentType is null )
        {
            return null;
        }

        if ( status == 200 && contentType is "text/html" or "text/plain" )
        {
            return new WebhookTextReply( Encoding.UTF8.GetString( body ), IsValidUtf8( body ) );
        }

        var (symbol, type) = MimeLookup.Lookup( contentType );
        return new WebhookAttachmentReply( body, $"attachment.{symbol}", type );
    }

    private static bool IsValidUtf8( byte[] body ) => Utf8.IsValid( body );
}
