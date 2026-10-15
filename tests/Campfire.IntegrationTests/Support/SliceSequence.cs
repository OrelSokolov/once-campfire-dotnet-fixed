namespace Campfire.IntegrationTests.Support;

/// <summary>The steps of one message create, in the order they happened.</summary>
internal static class SliceSequence
{
    /// <summary>
    /// The file's placement (and whether the index insert had committed by then), the jobs this message queued, and
    /// the broadcasts.
    /// </summary>
    public static List<string> Steps( SliceEvents events, long messageId, long blobId )
    {
        ArgumentNullException.ThrowIfNull( events );

        return [.. events.All.Where( line => IsStep( line, messageId, blobId ) )];
    }

    private static bool IsStep( string line, long messageId, long blobId ) =>
        line.StartsWith( "broadcast", StringComparison.Ordinal )
        || line.StartsWith( "unread", StringComparison.Ordinal )
        || line.EndsWith( "indexed", StringComparison.Ordinal )
        || line == $"upload blob {blobId}"
        || line.EndsWith( $"message {messageId}", StringComparison.Ordinal )
        || line == $"analyze_job blob {blobId}";
}
