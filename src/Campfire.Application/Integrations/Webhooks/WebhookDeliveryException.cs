using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Webhooks;

/// <summary>
/// Where Rails raises out of <c>Webhook#deliver</c>, failing the job: a bad URL, a connection failure other than a
/// timeout, a reply content type <c>Mime::Type</c> can't parse, a bot without a webhook, or (a bound of this port) a
/// reply larger than <see cref="WebhookDelivery.MaxReplySize"/>.
/// </summary>
public sealed class WebhookDeliveryException : Exception
{
    public WebhookDeliveryException( ) : this( RubyErrorClasses.StandardError, "Webhook delivery failed" )
    {
    }

    public WebhookDeliveryException( string message ) : this( RubyErrorClasses.StandardError, message )
    {
    }

    public WebhookDeliveryException( string message, Exception innerException ) : this( RubyErrorClasses.StandardError, message, innerException )
    {
    }

    public WebhookDeliveryException( string rubyClass, string message, Exception? innerException = null ) : base( message, innerException )
    {
        RubyClass = rubyClass;
    }

    /// <summary>The Ruby exception class, for the job's failure log.</summary>
    public string RubyClass { get; }
}
