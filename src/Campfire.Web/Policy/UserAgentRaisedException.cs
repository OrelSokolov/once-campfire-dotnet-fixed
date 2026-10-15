namespace Campfire.Web.Policy;

/// <summary>
/// The useragent gem raised (a <c>NoMethodError</c> on nil, an <c>ArgumentError</c>) instead of
/// answering. Rails turns it into a 500 wherever it escapes.
/// </summary>
public sealed class UserAgentRaisedException : Exception
{
    public UserAgentRaisedException( )
        : base( "The useragent gem raised NoMethodError." )
    {
    }

    public UserAgentRaisedException( string message )
        : base( message )
    {
    }

    public UserAgentRaisedException( string message, Exception innerException )
        : base( message, innerException )
    {
    }
}
