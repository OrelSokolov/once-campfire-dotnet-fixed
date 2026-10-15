using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;

namespace Campfire.Web.Cable;

/// <summary>
/// <c>ActionCable::Connection::Subscriptions</c> and the command side of <c>Channel::Base</c>:
/// one connection's subscriptions, keyed by the raw identifier string in subscription order, and
/// the <c>subscribe</c>/<c>unsubscribe</c>/<c>message</c> commands. Anything malformed raises in
/// Rails, which is logged and otherwise ignored: the connection stays open and nothing is sent.
/// </summary>
internal sealed class CableSubscriptions( CableConnection connection, CableServer server, ILogger logger )
{
    private readonly List<CableSubscription> _subscriptions = [];

    /// <summary><c>Subscriptions#execute_command</c>.</summary>
    public async Task ExecuteCommandAsync( string text, CancellationToken cancellationToken )
    {
        if ( !JsonPayload.TryParse( Encoding.UTF8.GetBytes( text ), out var parsed ) || parsed is not JsonObject data )
        {
            CableLog.CouldNotExecute( logger, text );
            return;
        }

        try
        {
            switch ( StringValue( data[CableMessageKeys.Command] ) )
            {
                case CableCommands.Subscribe:
                    await AddAsync( data, cancellationToken );
                    break;
                case CableCommands.Unsubscribe:
                    await RemoveAsync( data );
                    break;
                case CableCommands.Message:
                    await PerformActionAsync( data, cancellationToken );
                    break;
                default:
                    CableLog.UnrecognizedCommand( logger, text );
                    break;
            }
        }
        catch ( Exception error ) when ( error is not OutOfMemoryException )
        {
            // Subscriptions#execute_command rescues everything a command raises.
            CableLog.CouldNotExecute( logger, text, error );
        }
    }

    /// <summary><c>Connection::Base#handle_close</c> → <c>Subscriptions#unsubscribe_from_all</c>.</summary>
    public async Task UnsubscribeFromAllAsync( )
    {
        foreach ( var subscription in _subscriptions.ToList( ) )
        {
            try
            {
                await RemoveSubscriptionAsync( subscription );
            }
            catch ( Exception error ) when ( error is not OutOfMemoryException )
            {
                CableLog.UnsubscribeFailed( logger, subscription.Identifier, error );
            }
        }
    }

    /// <summary>
    /// <c>Subscriptions#add</c>. A repeated identifier (byte for byte) is ignored without a reply;
    /// a differently spelled identifier for the same channel is a separate subscription.
    /// </summary>
    private async Task AddAsync( JsonObject data, CancellationToken cancellationToken )
    {
        var identifier = StringValue( data[CableMessageKeys.Identifier] );
        if ( identifier is null )
        {
            CableLog.InvalidCommand( logger, "the identifier isn't a string" );
            return;
        }

        if ( IsPastBounds( identifier ) )
        {
            CableLog.InvalidCommand(
                logger,
                $"subscription refused at {_subscriptions.Count} subscriptions with an identifier of {identifier.Length} characters" );
            return;
        }

        if ( !CableParameters.TryParse( identifier, out var parameters ) )
        {
            CableLog.InvalidCommand( logger, $"invalid identifier {identifier}" );
            return;
        }

        if ( Find( identifier ) is not null )
        {
            return;
        }

        var channel = parameters.GetValueOrDefault( CableMessageKeys.Channel );
        var className = channel as string;
        var factory = className is null ? null : server.Channels.Find( className );
        if ( className is null || factory is null )
        {
            CableLog.ChannelNotFound( logger, channel?.ToString( ) ?? "nil" );
            return;
        }

        var subscription = new CableSubscription(
            connection,
            factory( server.Services ),
            CableChannelRegistry.Normalize( className ),
            identifier,
            parameters );
        _subscriptions.Add( subscription );
        await SubscribeToChannelAsync( subscription, cancellationToken );
    }

    /// <summary>
    /// <c>Channel::Base#subscribe_to_channel</c>: <c>subscribed</c>, then either the rejection
    /// (after the subscription is removed, which runs <c>unsubscribed</c>) or the confirmation.
    /// Streams the callback started deliver only after these frames are queued.
    /// </summary>
    private async Task SubscribeToChannelAsync( CableSubscription subscription, CancellationToken cancellationToken )
    {
        using var callback = connection.EnterCallback( );
        await subscription.Channel.SubscribedAsync( cancellationToken );

        if ( subscription.IsRejected )
        {
            await RemoveSubscriptionAsync( subscription );
            connection.Enqueue( CableProtocol.Rejection( subscription.Identifier ) );
        }
        else
        {
            connection.Enqueue( CableProtocol.Confirmation( subscription.Identifier ) );
        }
    }

    /// <summary><c>Subscriptions#remove</c>: no reply either way.</summary>
    private async Task RemoveAsync( JsonObject data )
    {
        var subscription = FindSubscription( data );
        if ( subscription is null )
        {
            return;
        }

        using var callback = connection.EnterCallback( );
        await RemoveSubscriptionAsync( subscription );
    }

    /// <summary>
    /// <c>Subscriptions#remove_subscription</c> → <c>Channel::Base#unsubscribe_from_channel</c>:
    /// <c>unsubscribed</c>, then <c>stop_all_streams</c>. Unlike Rails, a raising callback still
    /// ends the subscription, so its streams can't outlive it.
    /// </summary>
    private async Task RemoveSubscriptionAsync( CableSubscription subscription )
    {
        subscription.MarkUnsubscribed( );
        try
        {
            await subscription.Channel.UnsubscribedAsync( CancellationToken.None );
        }
        finally
        {
            subscription.StopAllStreams( );
            _subscriptions.Remove( subscription );
        }
    }

    /// <summary><c>Subscriptions#perform_action</c> → <c>Channel::Base#perform_action</c>.</summary>
    private async Task PerformActionAsync( JsonObject data, CancellationToken cancellationToken )
    {
        var subscription = FindSubscription( data );
        if ( subscription is null )
        {
            return;
        }

        if ( ActionData( data ) is not { } payload )
        {
            CableLog.InvalidCommand( logger, $"invalid data for {subscription.Identifier}" );
            return;
        }

        var action = ExtractAction( payload );
        if ( action is null )
        {
            CableLog.InvalidCommand( logger, $"invalid action for {subscription.Identifier}" );
            return;
        }

        using var callback = connection.EnterCallback( );
        var handled = !subscription.IsRejected && await subscription.Channel.PerformAsync( action, payload, cancellationToken );
        if ( !handled )
        {
            CableLog.UnableToProcess( logger, subscription.ChannelClassName, action );
        }
    }

    /// <summary>The subscription a command's <c>identifier</c> names; logged when there is none.</summary>
    private CableSubscription? FindSubscription( JsonObject data )
    {
        var identifierNode = data[CableMessageKeys.Identifier];
        var subscription = Find( StringValue( identifierNode ) );
        if ( subscription is null )
        {
            CableLog.SubscriptionNotFound( logger, identifierNode?.ToJsonString( ) );
        }

        return subscription;
    }

    /// <summary>A <c>message</c> command's <c>data</c>: a string holding a JSON object, or <c>null</c> for anything else.</summary>
    private static JsonObject? ActionData( JsonObject data )
    {
        var text = StringValue( data[CableMessageKeys.Data] );
        if ( text is null || !JsonPayload.TryParse( Encoding.UTF8.GetBytes( text ), out var decoded ) )
        {
            return null;
        }

        return decoded as JsonObject;
    }

    /// <summary>The bounds Rails doesn't have: too many subscriptions, or too long an identifier.</summary>
    private bool IsPastBounds( string identifier ) =>
        _subscriptions.Count >= server.Options.MaxSubscriptions
        || Encoding.UTF8.GetByteCount( identifier ) > server.Options.MaxIdentifierBytes;

    /// <summary>
    /// <c>(data["action"].presence || :receive).to_sym</c>: a blank action (missing, <c>null</c>,
    /// <c>false</c>, whitespace, <c>[]</c>, <c>{}</c>) is <c>receive</c>; a value with no
    /// <c>to_sym</c> (a number, <c>true</c>, a non-empty array or hash) raises, here <c>null</c>.
    /// </summary>
    private static string? ExtractAction( JsonObject payload )
    {
        return payload[CableMessageKeys.Action] switch
        {
            null => CableChannel.ReceiveAction,
            JsonValue value when value.GetValueKind( ) == JsonValueKind.String => PresentOrReceive( StringValue( value ) ),
            JsonValue value when value.GetValueKind( ) == JsonValueKind.False => CableChannel.ReceiveAction,
            JsonArray { Count: 0 } or JsonObject { Count: 0 } => CableChannel.ReceiveAction,
            _ => null,
        };
    }

    private static string PresentOrReceive( string? action )
    {
        if ( string.IsNullOrWhiteSpace( action ) )
        {
            return CableChannel.ReceiveAction;
        }

        return action;
    }

    private CableSubscription? Find( string? identifier ) =>
        identifier is null ? null : _subscriptions.Find( subscription => subscription.Identifier == identifier );

    private static string? StringValue( JsonNode? node ) =>
        node is JsonValue value && value.GetValueKind( ) == JsonValueKind.String ? value.GetValue<string>( ) : null;
}
