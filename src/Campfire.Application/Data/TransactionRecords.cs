namespace Campfire.Application.Data;

/// <summary>
/// The records a transaction enlisted, in the order they joined it, with their deferred work.
/// </summary>
/// <remarks>
/// Rails runs <c>before_committed!</c> and then, once <c>COMMIT</c> returns, <c>committed!</c> on each record in the
/// order it joined the transaction; a record joins before its own save callbacks run. Within a record, callbacks run
/// in the order they are defined (<c>run_after_transaction_callbacks_in_order_defined</c>), which the callers express
/// as an ordinal. A callback registered again for the same record and ordinal replaces the earlier one in place:
/// Rails runs each callback once per record however often the record was saved or touched.
/// </remarks>
internal sealed class TransactionRecords
{
    private const int MaximumBeforeCommitPasses = 8;

    private readonly List<Enlisted> _records = [];

    private readonly Dictionary<RecordKey, Enlisted> _byKey = [];

    private readonly List<Action<WriteScope>> _finalWrites = [];

    internal void AddFinalWrite( Action<WriteScope> step ) => _finalWrites.Add( step );

    public void Enlist( RecordKey key ) => Get( key );

    public void AddBeforeCommit( RecordKey key, int order, Action<WriteScope> step ) => Get( key ).BeforeCommit[order] = step;

    public void AddAfterCommit( RecordKey key, int order, string name, Action<AfterCommitContext> step ) =>
        Get( key ).AfterCommit[order] = (name, step);

    /// <summary>Runs the deferred before-commit work (<c>touch_later</c>'s touches), in enlistment order.</summary>
    public void RunBeforeCommit( WriteScope scope )
    {
        for ( var pass = 0; pass < MaximumBeforeCommitPasses; pass++ )
        {
            var anyStepRan = false;
            foreach ( var record in _records.ToList( ) )
            {
                foreach ( var order in record.BeforeCommit.Keys.ToList( ) )
                {
                    var step = record.BeforeCommit[order];
                    record.BeforeCommit.Remove( order );
                    step( scope );
                    anyStepRan = true;
                }
            }

            if ( !anyStepRan )
            {
                foreach ( var step in _finalWrites )
                {
                    step( scope );
                }

                return;
            }
        }

        throw new InvalidOperationException( "Before-commit work kept deferring more work" );
    }

    /// <summary>
    /// Runs the after-commit work in order. The first failure stops everything after it and is rethrown as
    /// <see cref="AfterCommitFailedException"/>.
    /// </summary>
    public void RunAfterCommit( AfterCommitContext context )
    {
        foreach ( var record in _records )
        {
            foreach ( var (name, step) in record.AfterCommit.Values )
            {
                try
                {
                    step( context );
                }
                catch ( Exception error )
                {
                    throw new AfterCommitFailedException(
                        $"The transaction committed, but the after-commit step {name} of {record.Key} failed: {error.Message}", error );
                }
            }
        }
    }

    private Enlisted Get( RecordKey key )
    {
        if ( !_byKey.TryGetValue( key, out var record ) )
        {
            record = new Enlisted( key );
            _byKey.Add( key, record );
            _records.Add( record );
        }

        return record;
    }

    private sealed class Enlisted( RecordKey key )
    {
        public RecordKey Key { get; } = key;

        public SortedList<int, Action<WriteScope>> BeforeCommit { get; } = [];

        public SortedList<int, (string Name, Action<AfterCommitContext> Step)> AfterCommit { get; } = [];
    }
}
