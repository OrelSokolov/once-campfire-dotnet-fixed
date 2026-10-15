namespace Campfire.Web.Tests.Cable.Support;

/// <summary>What the client saw: a text frame, the server's close frame, or the end of the socket.</summary>
public sealed record ReceivedFrame( string Kind, string? Text = null, int? CloseCode = null, string? CloseReason = null )
{
    public static ReceivedFrame End { get; } = new( "end" );

    public bool IsPing => Kind == "text" && Text!.StartsWith( "{\"type\":\"ping\"", StringComparison.Ordinal );

    public override string ToString( ) => Kind switch
    {
        "text" => Text!,
        "close" => $"close Some(({CloseCode}, \"{CloseReason}\"))",
        _ => Kind,
    };
}
