using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>Write helpers shared by the operations.</summary>
internal static class DataWrites
{
    private const int SqliteConstraint = 19;

    private const int SqliteConstraintPrimaryKey = 1555;

    private const int SqliteConstraintUnique = 2067;

    /// <summary>
    /// Runs a record's insert or update, turning a unique index violation into <see cref="RecordNotUniqueException"/>,
    /// as Active Record raises <c>ActiveRecord::RecordNotUnique</c> from <c>save</c>.
    /// </summary>
    public static T Save<T>( Func<T> write )
    {
        try
        {
            return write( );
        }
        catch ( SqliteException error ) when ( IsUniqueViolation( error ) )
        {
            throw new RecordNotUniqueException( error.Message, error );
        }
    }

    /// <summary>
    /// Checks that a record's own update or delete (<c>WHERE id = ?</c>) wrote its row. The row was read in the same
    /// immediate transaction on the only writer, so anything else is a bug, and the transaction rolls back.
    /// </summary>
    /// <exception cref="InvalidOperationException">No row, or more than one, was written.</exception>
    public static void ExpectOneRow( int rows, RecordKey record )
    {
        if ( rows != 1 )
        {
            throw new InvalidOperationException( $"Writing {record} changed {rows} rows instead of one" );
        }
    }

    public static bool IsUniqueViolation( Exception? error ) =>
        error is SqliteException { SqliteErrorCode: SqliteConstraint, SqliteExtendedErrorCode: SqliteConstraintUnique or SqliteConstraintPrimaryKey };
}
