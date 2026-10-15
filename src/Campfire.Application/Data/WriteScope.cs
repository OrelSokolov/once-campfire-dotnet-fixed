using Campfire.Application.Data.Hooks;

namespace Campfire.Application.Data;

/// <summary>
/// An operation's view of one write transaction: the session on the leased writer connection inside
/// <c>BEGIN IMMEDIATE</c>, the clock, the hooks, and the deferred work that runs before and after <c>COMMIT</c>.
/// </summary>
/// <remarks>
/// Callbacks are explicit calls in Rails order: an operation writes each row with its own
/// statement where Rails saves it, and registers what Rails defers with <see cref="BeforeCommit"/> and
/// <see cref="AfterCommit"/>.
/// </remarks>
public sealed class WriteScope
{
    private readonly TransactionRecords _records;

    internal WriteScope( CampfireDb db, TimeProvider clock, CampfireHooks hooks, TransactionRecords records )
    {
        Db = db;
        Clock = clock;
        Hooks = hooks;
        _records = records;
    }

    /// <summary>The session, with <see cref="CampfireDb.Transaction"/> set to this transaction.</summary>
    public CampfireDb Db { get; }

    public TimeProvider Clock { get; }

    public CampfireHooks Hooks { get; }

    /// <summary><c>Time.current</c>: UTC, truncated to microseconds as Active Record stores it.</summary>
    public DateTime Now => RailsTimestamp.Truncate( Clock.GetUtcNow( ).UtcDateTime );

    /// <summary>Records that <paramref name="record"/> joined the transaction, fixing its place in the commit order.</summary>
    public void Enlist( RecordKey record ) => _records.Enlist( record );

    /// <summary>Defers work to just before <c>COMMIT</c>, as <c>touch_later</c> defers its touch.</summary>
    public void BeforeCommit( RecordKey record, int order, Action<WriteScope> step ) => _records.AddBeforeCommit( record, order, step );

    /// <summary>Database writes after deferred touches, still inside the transaction.</summary>
    internal void BeforeCommitFinal( Action<WriteScope> step ) => _records.AddFinalWrite( step );

    /// <summary>Queues an <c>after_commit</c> callback of <paramref name="record"/>.</summary>
    public void AfterCommit( RecordKey record, int order, string name, Action<AfterCommitContext> step ) =>
        _records.AddAfterCommit( record, order, name, step );
}
