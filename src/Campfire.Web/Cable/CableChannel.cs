using System.Text.Json.Nodes;

namespace Campfire.Web.Cable;

/// <summary>
/// <c>ActionCable::Channel::Base</c>: one instance per subscription, created by the factory
/// registered under the channel's Ruby class name and driven by its connection, one command at a
/// time. Override <see cref="SubscribedAsync"/>, <see cref="UnsubscribedAsync"/> and
/// <see cref="PerformAsync"/>; the protected helpers are the Rails ones (<c>stream_from</c>,
/// <c>stream_for</c>, <c>reject</c>, <c>transmit</c>, <c>broadcast_to</c>).
/// <para>
/// Rails' <c>on_subscribe</c>/<c>on_unsubscribe</c> callbacks run after
/// <c>subscribed</c>/<c>unsubscribed</c>, so put them at the end of these methods, guarded on
/// <see cref="IsRejected"/> where the Ruby says <c>unless: :subscription_rejected?</c>. An
/// exception escaping a callback is logged and nothing is sent: a subscription whose
/// <c>subscribed</c> raised is neither confirmed nor rejected, and stays registered.
/// </para>
/// </summary>
public abstract class CableChannel
{
    /// <summary>
    /// <c>subscribed</c> as an action name. <c>Channel::Base.action_methods</c> is every public method a channel class
    /// adds, and most channels declare <c>subscribed</c> without <c>private</c>, so a client may perform it: it runs
    /// again (without the <c>on_subscribe</c> callbacks) and its <c>stream_from</c> adds a second delivery.
    /// </summary>
    public const string SubscribedAction = "subscribed";

    /// <summary>The action of a <c>perform</c> whose <c>data["action"]</c> is blank.</summary>
    public const string ReceiveAction = "receive";

    private CableSubscription? _subscription;

    /// <summary>The subscription this channel instance serves.</summary>
    public CableSubscription Subscription
    {
        get
        {
            if ( _subscription is null )
            {
                throw new InvalidOperationException( "The channel isn't attached to a subscription yet." );
            }

            return _subscription;
        }
    }

    /// <summary><c>subscribed</c>.</summary>
    public virtual ValueTask SubscribedAsync( CancellationToken cancellationToken ) => ValueTask.CompletedTask;

    /// <summary>
    /// <c>unsubscribed</c>. Also runs when the subscription is rejected, as in Rails
    /// (<c>reject_subscription</c> removes the subscription, which unsubscribes it), and when the
    /// connection closes.
    /// </summary>
    public virtual ValueTask UnsubscribedAsync( CancellationToken cancellationToken ) => ValueTask.CompletedTask;

    /// <summary>
    /// Dispatches a <c>perform</c> from the client: <paramref name="action"/> is
    /// <c>data["action"]</c>, or <see cref="ReceiveAction"/> when that's blank. Return <c>false</c> when it
    /// isn't one of the channel's actions, which Rails logs as "Unable to process".
    /// </summary>
    public virtual ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken ) =>
        ValueTask.FromResult( false );

    /// <summary><c>params</c>: the decoded identifier, <c>channel</c> included.</summary>
    protected IReadOnlyDictionary<string, object?> Params => Subscription.Params;

    /// <summary>The connection's identity (<c>identified_by</c>).</summary>
    protected CableIdentity Identity => Subscription.Identity;

    /// <summary><c>subscription_rejected?</c>.</summary>
    protected bool IsRejected => Subscription.IsRejected;

    protected void StreamFrom( string broadcasting ) => Subscription.StreamFrom( broadcasting );

    protected void StreamFor( params IEnumerable<string> broadcastables ) => Subscription.StreamFor( broadcastables );

    protected void StopAllStreams( ) => Subscription.StopAllStreams( );

    protected void Reject( ) => Subscription.Reject( );

    protected void Transmit( JsonNode? message ) => Subscription.Transmit( message );

    protected string BroadcastingFor( params IEnumerable<string> broadcastables ) => Subscription.BroadcastingFor( broadcastables );

    protected int BroadcastTo( IEnumerable<string> broadcastables, JsonNode? message ) => Subscription.BroadcastTo( broadcastables, message );

    internal void Attach( CableSubscription subscription ) => _subscription = subscription;

    /// <summary>
    /// What performing <see cref="SubscribedAction"/> runs: <c>subscribed</c> again, which a channel whose
    /// <c>subscribed</c> carries <c>on_subscribe</c> callbacks overrides with <c>subscribed</c> alone.
    /// </summary>
    protected virtual ValueTask ResubscribeAsync( CancellationToken cancellationToken ) => SubscribedAsync( cancellationToken );

    /// <summary>
    /// The <see cref="SubscribedAction"/> action of a channel whose <c>subscribed</c> is public: when
    /// <paramref name="action"/> is it, <see cref="ResubscribeAsync"/> runs and the action is handled.
    /// </summary>
    protected async ValueTask<bool> PerformSubscribedAsync( string action, CancellationToken cancellationToken )
    {
        if ( action != SubscribedAction )
        {
            return false;
        }

        await ResubscribeAsync( cancellationToken );
        return true;
    }
}

/// <summary>A channel whose connection identifies a <typeparamref name="TUser"/> (<c>current_user</c>).</summary>
public abstract class CableChannel<TUser> : CableChannel
    where TUser : class
{
    /// <summary><c>current_user</c>.</summary>
    protected TUser CurrentUser
    {
        get
        {
            if ( Identity.CurrentUser is not TUser user )
            {
                throw new InvalidOperationException(
                    $"The connection identifies a {Identity.CurrentUser.GetType( ).Name}, not a {typeof( TUser ).Name}." );
            }

            return user;
        }
    }
}
