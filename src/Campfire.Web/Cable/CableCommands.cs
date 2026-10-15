namespace Campfire.Web.Cable;

/// <summary>The <c>command</c> of a client message (<c>Subscriptions#execute_command</c>).</summary>
internal static class CableCommands
{
    public const string Subscribe = "subscribe";

    public const string Unsubscribe = "unsubscribe";

    public const string Message = "message";
}
