using Campfire.Application.Ruby;

namespace Campfire.Application.RichText;

/// <summary>
/// Something Ruby would have raised while rendering a rich text body. Callers mirror what the Rails
/// code does with the exception: <c>MessagesHelper#message_presentation</c> rescues everything and
/// renders an empty string, while <c>to_plain_text</c> and the editor raise to their callers.
/// </summary>
/// <param name="rubyClass">The Ruby exception class (and, for <c>NoMethodError</c>, the method) Rails raises.</param>
/// <param name="isUnrenderable">Whether Rails' rescue can't log the exception's message.</param>
public class RichTextException( string rubyClass, bool isUnrenderable = false )
    : Exception( isUnrenderable ? $"raised {rubyClass} with an unloggable message" : $"raised {rubyClass}" )
{
    /// <summary>The Ruby exception class (and, for <c>NoMethodError</c>, the method) Rails raises here.</summary>
    public string RubyClass { get; } = rubyClass;

    /// <summary>
    /// Rails raised with a message that isn't valid UTF-8, so the rescue's own logging raises too and
    /// the message renders as <c>messages/_unrenderable.html.erb</c>.
    /// </summary>
    public bool IsUnrenderable { get; } = isUnrenderable;

    /// <summary>Ruby's <c>NoMethodError</c> for calling <paramref name="method"/> (on <c>nil</c>, or on an object without it).</summary>
    internal static RichTextException NoMethod( string method ) => new( $"{RubyErrorClasses.NoMethodError}: {method}" );
}
