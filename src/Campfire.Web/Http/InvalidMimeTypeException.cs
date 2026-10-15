namespace Campfire.Web.Http;

/// <summary><c>Mime::Type::InvalidMimeType</c>; Rails answers it with 406 (<c>MimeNegotiation::InvalidType</c>).</summary>
public sealed class InvalidMimeTypeException : Exception
{
    public InvalidMimeTypeException( string value )
        : base( $"\"{value}\" is not a valid MIME type" )
    {
    }

    public InvalidMimeTypeException( )
    {
    }

    public InvalidMimeTypeException( string message, Exception innerException )
        : base( message, innerException )
    {
    }
}
