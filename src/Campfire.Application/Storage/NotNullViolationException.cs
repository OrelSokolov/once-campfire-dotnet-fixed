namespace Campfire.Application.Storage;

/// <summary><c>ActiveRecord::NotNullViolation</c>: an insert left a <c>NOT NULL</c> column empty (a 500, as in Rails).</summary>
public sealed class NotNullViolationException : Exception
{
    public NotNullViolationException( string message )
        : base( message )
    {
    }

    public NotNullViolationException( )
    {
    }

    public NotNullViolationException( string message, Exception innerException )
        : base( message, innerException )
    {
    }
}
