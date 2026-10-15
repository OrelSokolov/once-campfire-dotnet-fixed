namespace Campfire.Web.Cable;

/// <summary>
/// The <c>type</c>s of <c>ActionCable::INTERNAL[:message_types]</c> that are built or read as JSON values: a disconnect
/// is both sent to clients and broadcast on a connection's internal channel. The other frames are
/// <see cref="CableProtocol"/>'s fixed templates.
/// </summary>
internal static class CableMessageTypes
{
    public const string Disconnect = "disconnect";
}
