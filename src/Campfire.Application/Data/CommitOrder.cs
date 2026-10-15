namespace Campfire.Application.Data;

/// <summary>Commit-order positions shared by every model (<see cref="WriteScope.BeforeCommit"/>).</summary>
internal static class CommitOrder
{
    /// <summary>
    /// A deferred <c>touch</c> (<c>touch_later</c>): a record's only before-commit work, so every model runs it in the
    /// same place.
    /// </summary>
    public const int Touch = 1;
}
