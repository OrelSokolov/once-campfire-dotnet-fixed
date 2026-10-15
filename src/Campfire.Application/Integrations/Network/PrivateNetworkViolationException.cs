namespace Campfire.Application.Integrations.Network;

/// <summary>
/// <c>RestrictedHTTP::Violation</c>: the host only resolves to blocked addresses, or surfguard rejected it outright.
/// </summary>
public sealed class PrivateNetworkViolationException : Exception
{
    public PrivateNetworkViolationException( ) : this( "" )
    {
    }

    public PrivateNetworkViolationException( string host ) : base( $"Attempt to access private IP via {host}" )
    {
        Host = host;
    }

    public PrivateNetworkViolationException( string message, Exception innerException ) : base( message, innerException )
    {
        Host = "";
    }

    public string Host { get; }
}
