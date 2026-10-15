namespace Campfire.Web.Cable;

/// <summary><c>HeartbeatChannel</c> (<c>app/channels/heartbeat_channel.rb</c>): subscribes, confirms and does nothing else.</summary>
public sealed class HeartbeatChannel : CableChannel
{
    public const string ClassName = "HeartbeatChannel";
}
