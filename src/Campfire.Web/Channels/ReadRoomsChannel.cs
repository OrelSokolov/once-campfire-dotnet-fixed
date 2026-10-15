namespace Campfire.Web.Channels;

/// <summary>
/// <c>ReadRoomsChannel</c>: <c>stream_from "user_#{current_user.id}_reads"</c>, where <c>PresenceChannel</c> says a room
/// was read.
/// </summary>
public sealed class ReadRoomsChannel( ) : UserStreamChannel( StreamSuffix )
{
    /// <summary>The Ruby class name clients subscribe with (<c>ReadRoomsChannel</c>).</summary>
    public const string ClassName = "ReadRoomsChannel";

    private const string StreamSuffix = "reads";

    public static string StreamNameFor( long userId ) => UserStreamName( userId, StreamSuffix );
}
