using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>
/// The held writer connection for database operations. Each <see cref="Transaction{T}"/> runs in
/// <c>BEGIN IMMEDIATE</c> (Rails' <c>default_transaction_mode: immediate</c>), commits, and only then runs the
/// transaction's after-commit work, in order. A rollback runs none of it. An operation that Rails splits into
/// several transactions runs several, one after another, under the same lease.
/// </summary>
public sealed class WriterLease : IDisposable
{
    private readonly CampfireDatabase _database;

    private readonly SqliteConnection _connection;

    private bool _disposed;

    internal WriterLease( CampfireDatabase database, SqliteConnection connection, CampfireDb db )
    {
        _database = database;
        _connection = connection;
        Db = db;
    }

    /// <summary>The session on the writer connection, for reads and single statements between transactions.</summary>
    public CampfireDb Db { get; }

    /// <summary>
    /// Runs <paramref name="work"/> in one immediate transaction, commits, then runs its after-commit work.
    /// </summary>
    /// <exception cref="AfterCommitFailedException">The transaction committed, but an after-commit step failed.</exception>
    public T Transaction<T>( Func<WriteScope, T> work )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        if ( Db.Transaction is not null )
        {
            throw new InvalidOperationException( "Transactions do not nest; run the inner work in the outer scope" );
        }

        var records = new TransactionRecords( );
        var result = RunAndCommit( work, records );

        try
        {
            records.RunAfterCommit( new AfterCommitContext( Db, _database.Clock, _database.Hooks ) );
        }
        finally
        {
            // The checkpointer learns what this transaction and its after-commit statements appended to the WAL.
            _database.NoteWrites( );
        }

        return result;
    }

    /// <inheritdoc cref="Transaction{T}(Func{WriteScope, T})"/>
    public void Transaction( Action<WriteScope> work ) => Transaction( scope =>
    {
        work( scope );
        return true;
    } );

    public void Dispose( )
    {
        if ( _disposed )
        {
            return;
        }

        _disposed = true;
        Db.Release( );
        try
        {
            // Statements run on the lease outside a transaction (Rails' single-statement writes) append to the WAL too.
            _database.NoteWrites( );
        }
        finally
        {
            _database.ReleaseWriter( );
        }
    }

    private T RunAndCommit<T>( Func<WriteScope, T> work, TransactionRecords records )
    {
        // BeginTransaction( ) issues BEGIN IMMEDIATE; only BeginTransaction( deferred: true ) would not. Disposing it
        // without a commit rolls back.
        using var transaction = _connection.BeginTransaction( );
        Db.Transaction = transaction;
        try
        {
            var scope = new WriteScope( Db, _database.Clock, _database.Hooks, records );
            var result = work( scope );
            records.RunBeforeCommit( scope );

            transaction.Commit( );
            return result;
        }
        finally
        {
            Db.Transaction = null;
        }
    }
}
