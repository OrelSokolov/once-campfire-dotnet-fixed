namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>A request as the server saw it: header lines as sent (name case kept), in order.</summary>
internal sealed record ReceivedRequest( string Method, string Target, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body )
{
    public string? Header( string name ) => Headers.FirstOrDefault( header => header.Key.Equals( name, StringComparison.OrdinalIgnoreCase ) ).Value;
}
