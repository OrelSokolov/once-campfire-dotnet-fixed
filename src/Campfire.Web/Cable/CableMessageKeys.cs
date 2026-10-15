namespace Campfire.Web.Cable;

/// <summary>
/// The keys of Action Cable's JSON messages: the frames the server sends (<c>Connection::Base</c>, <c>Channel::Base</c>),
/// the commands a client sends (<c>Subscriptions#execute_command</c>) and the identifier's <c>channel</c>. The frame
/// templates in <see cref="CableProtocol"/> spell them inline, as the wire shows them.
/// </summary>
internal static class CableMessageKeys
{
    public const string Type = "type";

    public const string Identifier = "identifier";

    public const string Message = "message";

    public const string Reason = "reason";

    public const string Reconnect = "reconnect";

    public const string Command = "command";

    public const string Data = "data";

    /// <summary>The identifier's channel class name (<c>params[:channel]</c>).</summary>
    public const string Channel = "channel";

    /// <summary>A <c>message</c> command's action (<c>data["action"]</c>).</summary>
    public const string Action = "action";
}
