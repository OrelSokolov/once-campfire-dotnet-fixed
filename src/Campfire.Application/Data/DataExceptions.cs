using System.Globalization;

namespace Campfire.Application.Data;

/// <summary><c>ActiveRecord::RecordNotFound</c>: a lookup that Rails makes with <c>find</c> or <c>find_by!</c> found nothing.</summary>
public sealed class RecordNotFoundException : Exception
{
    /// <summary>The model name of a lookup that names none.</summary>
    private const string AnyRecord = "Record";

    /// <summary>How Rails starts the message of a lookup that found nothing: <c>Couldn't find {model}</c>.</summary>
    private const string CouldNotFindPrefix = "Couldn't find ";

    public RecordNotFoundException( ) : this( AnyRecord )
    {
    }

    /// <summary><c>Couldn't find {model}</c>, the message of <c>find_by!</c>.</summary>
    public RecordNotFoundException( string model ) : this( model, CouldNotFindPrefix + model )
    {
    }

    public RecordNotFoundException( string message, Exception innerException ) : base( message, innerException )
    {
        Model = AnyRecord;
    }

    private RecordNotFoundException( string model, string message ) : base( message )
    {
        Model = model;
    }

    /// <summary>The Rails model name, e.g. <c>Message</c>.</summary>
    public string Model { get; }

    /// <summary><c>Couldn't find {model} with 'id'={id}</c>, the message of <c>find(id)</c>.</summary>
    public static RecordNotFoundException WithId( string model, object id ) =>
        new( model, string.Create( CultureInfo.InvariantCulture, $"{CouldNotFindPrefix}{model} with 'id'={id}" ) );
}

/// <summary><c>ActiveRecord::RecordInvalid</c>: validations failed, and the transaction rolls back.</summary>
public sealed class RecordInvalidException : Exception
{
    public RecordInvalidException( ) : this( new ValidationErrors( ) )
    {
    }

    public RecordInvalidException( ValidationErrors errors ) : base( $"Validation failed: {errors}" )
    {
        Errors = errors;
    }

    public RecordInvalidException( string message ) : base( message )
    {
        Errors = new ValidationErrors( );
    }

    public RecordInvalidException( string message, Exception innerException ) : base( message, innerException )
    {
        Errors = new ValidationErrors( );
    }

    public ValidationErrors Errors { get; }
}

/// <summary>
/// <c>ActiveRecord::RecordNotUnique</c>: an insert or update hit a unique index. Controllers rescue it, as
/// <c>UsersController#create</c> and <c>FirstRunsController#create</c> do.
/// </summary>
public sealed class RecordNotUniqueException : Exception
{
    public RecordNotUniqueException( )
    {
    }

    public RecordNotUniqueException( string message ) : base( message )
    {
    }

    public RecordNotUniqueException( string message, Exception innerException ) : base( message, innerException )
    {
    }
}

/// <summary>
/// The write committed, then an after-commit step failed. The remaining steps did not run, as in Rails, where an
/// exception from an <c>after_commit</c> callback skips the rest of that record's callbacks and every later record's.
/// </summary>
/// <remarks>The database changes are durable: callers must not treat this as a rollback.</remarks>
public sealed class AfterCommitFailedException : Exception
{
    public AfterCommitFailedException( )
    {
    }

    public AfterCommitFailedException( string message ) : base( message )
    {
    }

    public AfterCommitFailedException( string message, Exception innerException ) : base( message, innerException )
    {
    }
}

/// <summary>The database file is not a Campfire database this build can run on.</summary>
public sealed class SchemaMismatchException : Exception
{
    public SchemaMismatchException( )
    {
    }

    public SchemaMismatchException( string message ) : base( message )
    {
    }

    public SchemaMismatchException( string message, Exception innerException ) : base( message, innerException )
    {
    }
}
