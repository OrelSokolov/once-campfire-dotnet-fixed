namespace Campfire.Application.Integrations.Network;

/// <summary><c>Surfguard::Unresolvable</c>: the lookup failed or came back empty.</summary>
public sealed class HostUnresolvableException : Exception
{
    public HostUnresolvableException( ) : base( "Host could not be resolved" )
    {
    }

    public HostUnresolvableException( string message ) : base( message )
    {
    }

    public HostUnresolvableException( string message, Exception innerException ) : base( message, innerException )
    {
    }
}
