namespace Campfire.Web.Http;

/// <summary>A failure the error pages answer with <see cref="Status"/> instead of a 500.</summary>
public class HttpFailureException( int status, string message, Exception? innerException = null ) : Exception( message, innerException )
{
    public int Status { get; } = status;
}
