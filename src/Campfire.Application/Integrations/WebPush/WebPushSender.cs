using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text;

using Campfire.Application.Integrations.Network;
using Campfire.Application.Pushes;
using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.WebPush;

/// <summary>
/// <c>WebPush::Notification#deliver</c> and the gem request it makes (web-push 3.1.0 with the
/// <c>WebPush::PersistentRequest</c> patch in <c>config/initializers/web_push.rb</c>): the endpoint is resolved on the
/// worker, only for a permitted <c>https://…:443</c> push service, through the private network guard, and the
/// connection goes to that address with no proxy (<c>http.ipaddr = endpoint_ip</c>, <c>Net::HTTP.new(host, port, nil)</c>).
/// </summary>
/// <remarks>
/// Time bounds (documented in docs/compatibility.md#deliberate-differences-and-bounds; from the Rust port's
/// <c>dad928c</c>): each connect and read waits at most <see cref="StepTimeout"/> (10 s) and a whole delivery at most
/// <see cref="DeliveryDeadline"/> (30 s), where the gem leaves <c>Net::HTTP</c>'s 60 seconds per step. A delivery that
/// runs out raises <c>Net::ReadTimeout</c>, which keeps the subscription.
/// </remarks>
public sealed class WebPushSender : IDisposable
{
    /// <summary>Each connect and each read.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds( 10 );

    /// <summary>A whole delivery, resolution included, however the push service trickles its reply.</summary>
    public static readonly TimeSpan DeliveryDeadline = TimeSpan.FromSeconds( 30 );

    /// <summary><c>WebPush::Request#default_options[:ttl]</c>: four weeks.</summary>
    public const int TimeToLive = 60 * 60 * 24 * 7 * 4;

    /// <summary>The <c>urgency:</c> <c>WebPush::Notification#deliver</c> asks for.</summary>
    public const string Urgency = "high";

    private readonly OutboundNetwork _network;

    private readonly VapidKeys _vapid;

    private readonly TimeProvider _clock;

    private readonly TimeSpan _deadline;

    private readonly HttpMessageInvoker _http;

    public WebPushSender( OutboundNetwork network, VapidKeys vapid, TimeProvider? clock = null )
        : this( network, vapid, clock, StepTimeout, DeliveryDeadline )
    {
    }

    internal WebPushSender( OutboundNetwork network, VapidKeys vapid, TimeProvider? clock, TimeSpan stepTimeout, TimeSpan deadline )
    {
        ArgumentNullException.ThrowIfNull( network );
        ArgumentNullException.ThrowIfNull( vapid );

        _network = network;
        _vapid = vapid;
        _clock = clock ?? TimeProvider.System;
        _deadline = deadline;
        _http = OutboundHttpClients.CreatePinned( network, stepTimeout, stepTimeout );
    }

    /// <summary>
    /// <c>WebPush::Notification#deliver</c>: null when the endpoint isn't a permitted push service or doesn't resolve to
    /// a public address (nothing is sent); otherwise the push service's 2xx status. Throws
    /// <see cref="WebPushDeliveryException"/> as the gem raises.
    /// </summary>
    public async Task<int?> DeliverAsync( WebPushNotification notification, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( notification );

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        deadline.CancelAfter( _deadline );
        try
        {
            if ( await ResolvedEndpointAddressAsync( notification.Endpoint, deadline.Token ) is not { } address )
            {
                return null;
            }

            return await PayloadSendAsync( notification, address, deadline.Token );
        }
        catch ( OperationCanceledException error ) when ( !cancellationToken.IsCancellationRequested )
        {
            throw new WebPushDeliveryException( RubyErrorClasses.ReadTimeout, $"no reply within {_deadline.TotalSeconds} seconds", innerException: error );
        }
    }

    public void Dispose( ) => _http.Dispose( );

    /// <summary>
    /// <c>Push::Subscription#resolved_endpoint_ip</c>: <c>PrivateNetworkGuard.resolve(host)</c> for a permitted endpoint,
    /// null for anything else or a host without a public address. <c>Users::PushSubscriptionsController#create</c>
    /// validates with it before taking the writer lease:
    /// <c>PushEndpoints.Validate( endpoint, _ =&gt; address?.ToString( ) )</c>.
    /// </summary>
    public async Task<IPAddress?> ResolvedEndpointAddressAsync( string? endpoint, CancellationToken cancellationToken )
    {
        if ( PushEndpoints.PermittedHost( endpoint ) is not { } host )
        {
            return null;
        }

        return await PrivateNetworkGuard.TryResolveAsync( _network.Resolver, host, cancellationToken );
    }

    /// <summary>
    /// <c>WebPush.payload_send(message:, endpoint:, endpoint_ip:, p256dh:, auth:, vapid:, urgency: "high")</c> through the
    /// pinned-address branch of <c>WebPush::PersistentRequest#perform</c>.
    /// </summary>
    private async Task<int> PayloadSendAsync( WebPushNotification notification, IPAddress address, CancellationToken cancellationToken )
    {
        var message = Encoding.UTF8.GetBytes( notification.EncodedMessage( ) );
        var body = WebPushEncryption.Encrypt( message, notification.P256dhKey, notification.AuthKey );

        // The endpoint was validated as https on port 443, so the port is always a valid one here
        if ( RubyUri.TryParse( notification.Endpoint!, out var uri ) != RubyUriError.None
            || uri!.Host is not { Length: > 0 } host
            || !uri.TryGetTcpPort( out var port ) )
        {
            throw new WebPushDeliveryException( RubyErrorClasses.InvalidUri, RubyUri.InvalidUriMessage( notification.Endpoint ) );
        }

        var audience = $"{uri.Scheme!.ToLowerInvariant( )}://{host}";
        var headers = new List<KeyValuePair<string, string>>
        {
            new( HttpHeaderNames.ContentType, MediaTypeNames.Application.Octet ),
            new( HttpHeaderNames.TimeToLive, TimeToLive.ToString( CultureInfo.InvariantCulture ) ),
            new( HttpHeaderNames.Urgency, Urgency ),
            new( HttpHeaderNames.ContentEncoding, "aes128gcm" ),
            new( HttpHeaderNames.ContentLength, body.Length.ToString( CultureInfo.InvariantCulture ) ),
            new( HttpHeaderNames.Authorization, _vapid.Authorization( audience, _clock.GetUtcNow( ).ToUnixTimeSeconds( ) ) ),
        };
        var endpoint = new NetHttpEndpoint( Https: true, host, port, address );
        using var request = NetHttpRequest.Create(
            HttpMethod.Post,
            endpoint,
            NetHttpRequest.RequestUri( uri ),
            HostHeaderPlacement.AfterConnection,
            connectionClose: true,
            headers,
            body );
        try
        {
            using var response = await NetHttpRequest.SendAsync( _http, request, cancellationToken );
            var error = WebPushDeliveryException.ForResponse( response.Status, response.Reason, host );
            if ( error is not null )
            {
                throw error;
            }

            return response.Status;
        }
        catch ( NetHttpException error )
        {
            throw WebPushDeliveryException.From( error );
        }
    }
}
