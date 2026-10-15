namespace Campfire.Web.Cable;

/// <summary>What happened to a frame offered to a connection's outbox.</summary>
internal enum CableEnqueueResult
{
    Accepted,

    /// <summary>The backlog bound was hit: the client is too slow and must be disconnected.</summary>
    Full,

    /// <summary>The connection is closing; the frame is dropped, as Rails drops writes to a closing socket.</summary>
    Closed,
}
