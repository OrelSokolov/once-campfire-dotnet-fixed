namespace Campfire.Application.Integrations.Opengraph;

/// <summary>A fetch Rails abandons by raising (<c>TooManyRedirectsError</c>, <c>RedirectDeniedError</c>, a bad URI); the callers rescue it.</summary>
internal sealed class OpengraphFetchException : Exception
{
    public OpengraphFetchException( ) : this( "Opengraph::Fetch error" )
    {
    }

    public OpengraphFetchException( string message ) : base( message )
    {
    }

    public OpengraphFetchException( string message, Exception innerException ) : base( message, innerException )
    {
    }
}
