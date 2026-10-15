namespace Campfire.Web.Cable;

/// <summary><c>ActionCable::INTERNAL[:disconnect_reasons]</c>.</summary>
public static class CableDisconnectReasons
{
    public const string Unauthorized = "unauthorized";

    public const string InvalidRequest = "invalid_request";

    public const string ServerRestart = "server_restart";

    public const string Remote = "remote";
}
