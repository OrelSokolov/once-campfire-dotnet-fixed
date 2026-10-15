using System.Text.Json.Nodes;
using Campfire.Web.Cable;

namespace Campfire.Web.Channels;

/// <summary>
/// A channel scoped per user: <c>stream_from "user_#{current_user.id}_&lt;suffix&gt;"</c>. Its public <c>subscribed</c>
/// is an action too, which streams the same name again.
/// </summary>
public abstract class UserStreamChannel( string suffix ) : CableChannel<CableUser>
{
    public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
    {
        StreamFrom( UserStreamName( CurrentUser.Id, suffix ) );
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken ) =>
        PerformSubscribedAsync( action, cancellationToken );

    /// <summary><c>"user_#{user_id}_&lt;suffix&gt;"</c>.</summary>
    protected static string UserStreamName( long userId, string suffix ) => $"user_{userId}_{suffix}";
}
