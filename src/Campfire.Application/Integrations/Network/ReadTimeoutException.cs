using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Network;

/// <summary>A read waited longer than its client's read timeout (<c>Net::ReadTimeout</c>).</summary>
internal sealed class ReadTimeoutException : IOException
{
    public ReadTimeoutException( ) : base( RubyErrorClasses.ReadTimeout )
    {
    }

    public ReadTimeoutException( string message ) : base( message )
    {
    }

    public ReadTimeoutException( string message, Exception innerException ) : base( message, innerException )
    {
    }
}
