namespace Campfire.Application.Data;

/// <summary>When the <see cref="WalCheckpointer"/> checkpoints.</summary>
/// <param name="CheckpointPages">WAL pages the writer appends between two checkpoints.</param>
/// <param name="WalLimitPages">The WAL size at which writes stop for a <c>TRUNCATE</c> checkpoint.</param>
/// <param name="IdleInterval">How long appended pages may wait for a checkpoint when writes are light.</param>
internal sealed record CheckpointSettings( int CheckpointPages, int WalLimitPages, TimeSpan IdleInterval )
{
    /// <summary>
    /// SQLite's default <c>wal_autocheckpoint</c>, which Rails keeps: a checkpoint per 1,000 WAL pages. The limit,
    /// 10,000 pages (about 40 MB at the 4 KiB page size), stays below <c>journal_size_limit</c> (64 MiB).
    /// </summary>
    public static CheckpointSettings Default { get; } = new( 1000, 10_000, TimeSpan.FromMilliseconds( 500 ) );
}
