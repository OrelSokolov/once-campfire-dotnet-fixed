namespace Campfire.Web.Cable;

/// <summary>
/// <c>ApplicationCable::Channel</c> itself: a channel with no callbacks, which any client may
/// subscribe to and be confirmed (Rails constantizes any <c>Channel::Base</c> subclass).
/// </summary>
public sealed class ApplicationCableChannel : CableChannel
{
    public const string ClassName = "ApplicationCable::Channel";
}
