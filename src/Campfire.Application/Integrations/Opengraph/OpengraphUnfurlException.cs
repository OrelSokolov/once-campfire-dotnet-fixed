using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary>Where Rails raises out of <c>UnfurlLinksController#create</c>, so it answers 500.</summary>
public sealed class OpengraphUnfurlException : Exception
{
    public OpengraphUnfurlException( ) : this( RubyErrorClasses.StandardError )
    {
    }

    public OpengraphUnfurlException( string rubyClass ) : base( $"raised {rubyClass}" )
    {
        RubyClass = rubyClass;
    }

    public OpengraphUnfurlException( string rubyClass, Exception innerException ) : base( $"raised {rubyClass}", innerException )
    {
        RubyClass = rubyClass;
    }

    /// <summary>The Ruby exception class: <c>URI::InvalidComponentError</c>, <c>NoMethodError</c> or <c>ArgumentError</c>.</summary>
    public string RubyClass { get; }
}
