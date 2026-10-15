namespace Campfire.Web.Channels;

/// <summary>
/// <c>UnreadRoomsChannel</c>: scoped per user, like <c>ReadRoomsChannel</c>; <c>Message#broadcast_create</c> sends
/// <c>{ roomId: }</c> to each member.
/// </summary>
public sealed class UnreadRoomsChannel( ) : UserStreamChannel( StreamSuffix )
{
    /// <summary>The Ruby class name clients subscribe with (<c>UnreadRoomsChannel</c>).</summary>
    public const string ClassName = "UnreadRoomsChannel";

    private const string StreamSuffix = "unreads";

    /// <summary><c>UnreadRoomsChannel.stream_name_for(user_id)</c>.</summary>
    public static string StreamNameFor( long userId ) => UserStreamName( userId, StreamSuffix );
}
