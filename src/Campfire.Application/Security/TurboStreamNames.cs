namespace Campfire.Application.Security;

/// <summary>
/// <c>Turbo::StreamsChannel</c>'s signed stream names: the streamables joined with <c>:</c> (<c>stream_name_from</c>),
/// signed for the page (<c>signed_stream_name</c>) and verified when a client subscribes.
/// </summary>
public sealed class TurboStreamNames( ITokenProtector tokens )
{
    public const string Purpose = "turbo-stream";

    /// <summary><c>stream_name_from(streamables)</c>.</summary>
    public static string StreamName( params IEnumerable<string> streamables ) => string.Join( ":", streamables );

    /// <summary><c>signed_stream_name(streamables)</c>.</summary>
    public string Sign( params IEnumerable<string> streamables ) => tokens.Protect( StreamName( streamables ), Purpose );

    /// <summary><c>verified_stream_name</c>: the stream name, or null when the signature does not verify.</summary>
    public string? Verify( string signedStreamName ) => tokens.Unprotect( signedStreamName, Purpose, DateTimeOffset.UtcNow );
}
