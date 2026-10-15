using Campfire.Application.Ruby;

namespace Campfire.Application.RichText;

/// <summary>A limit of Nokogiri's HTML5 parser (Gumbo), or a bound of this port.</summary>
public enum RichTextParseError
{
    /// <summary>Gumbo's <c>max_tree_depth</c>: "Document tree depth limit exceeded".</summary>
    TreeDepthExceeded,

    /// <summary>Gumbo's <c>max_attributes</c>: "Attributes per element limit exceeded".</summary>
    TooManyAttributes,

    /// <summary>This port's bound on the markup one parse accepts (Rails has none).</summary>
    InputTooLong,
}

/// <summary>Nokogiri raises <c>ArgumentError</c> for markup beyond Gumbo's limits.</summary>
/// <param name="error">Which limit the markup exceeded.</param>
public sealed class RichTextParseException( RichTextParseError error ) : RichTextException( RubyErrorClasses.ArgumentError )
{
    public RichTextParseError Error { get; } = error;
}
