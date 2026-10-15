using System.Diagnostics.CodeAnalysis;

namespace Campfire.Web.Channels;

/// <summary>
/// <c>RoomStreamsAreAuthorized</c> (<c>app/channels/concerns/room_streams_are_authorized.rb</c>) /
/// <c>RoomMessagesChannel.guarded_stream?</c>: a room's message stream (<c>&lt;room gid param&gt;:messages</c>) may only
/// be subscribed through <see cref="RoomMessagesChannel"/>, which checks membership; <c>Turbo::StreamsChannel</c>
/// turns it away. The suffix is <c>RoomMessagesChannel::STREAM_SUFFIX</c>, <see cref="RoomBroadcasts.MessagesStream"/>.
/// </summary>
public static class RoomStreamsAreAuthorized
{
    /// <summary><c>stream_name.to_s.split(":", 2).second == "messages"</c>.</summary>
    public static bool IsGuardedStream( string streamName ) => GlobalIdParamLength( streamName ) >= 0;

    /// <summary>
    /// A guarded stream name split as <c>split(":", 2)</c>: <paramref name="gidParam"/> is the part before the first
    /// <c>:</c>, the room's GID param. False, with no part, for a name that isn't a room's message stream.
    /// </summary>
    public static bool TrySplit( string streamName, [NotNullWhen( true )] out string? gidParam )
    {
        var length = GlobalIdParamLength( streamName );
        gidParam = length >= 0 ? streamName[..length] : null;
        return gidParam is not null;
    }

    /// <summary>Where the first <c>:</c> is when <c>messages</c> follows it; -1 otherwise.</summary>
    private static int GlobalIdParamLength( string streamName )
    {
        ArgumentNullException.ThrowIfNull( streamName );

        var separator = streamName.IndexOf( ':' );
        if ( separator < 0 || !streamName.AsSpan( separator + 1 ).SequenceEqual( RoomBroadcasts.MessagesStream ) )
        {
            return -1;
        }

        return separator;
    }
}
