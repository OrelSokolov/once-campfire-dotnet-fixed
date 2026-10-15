namespace Campfire.Web.Http;

/// <summary><c>ActionDispatch::RemoteIp::IpSpoofAttackError</c>; Rails answers it with 500.</summary>
public sealed class IpSpoofAttackException : Exception
{
    public IpSpoofAttackException( string message )
        : base( message )
    {
    }

    public IpSpoofAttackException( )
    {
    }

    public IpSpoofAttackException( string message, Exception innerException )
        : base( message, innerException )
    {
    }
}
