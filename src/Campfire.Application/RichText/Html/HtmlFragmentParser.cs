using Campfire.Application.RichText.Html.Parsing;

namespace Campfire.Application.RichText.Html;

/// <summary>
/// Parses markup the way Nokogiri's HTML5 fragment parser does for Action Text: AngleSharp's inert
/// tokenizer (no scripting, no resource loading) feeding a port of Gumbo's tree builder
/// (<see cref="GumboTreeBuilder"/>).
/// </summary>
/// <remarks>
/// AngleSharp's own tree builder was the first choice, but in version 1.8.2 it departs from Gumbo in
/// ways its public API can't correct: in a fragment it keeps a breakout tag such as <c>&lt;p&gt;</c>
/// inside SVG or MathML as a foreign element, it follows the newer customizable-<c>&lt;select&gt;</c>
/// rules (content inside an option is kept, where Gumbo drops the tags), it still expands
/// <c>&lt;isindex&gt;</c>, it lacks <c>&lt;search&gt;</c>, and it exposes neither the stack of open
/// elements (for Gumbo's depth limit) nor a context element's namespace.
/// </remarks>
internal static class HtmlFragmentParser
{
    /// <summary><c>Nokogiri::Gumbo::DEFAULT_MAX_TREE_DEPTH</c>.</summary>
    public const int MaxTreeDepth = 400;

    /// <summary><c>Nokogiri::Gumbo::DEFAULT_MAX_ATTRIBUTES</c>.</summary>
    public const int MaxAttributes = 400;

    /// <summary>
    /// The longest markup one parse accepts, in UTF-16 code units: a backstop above
    /// <see cref="RichTextLimits.MaxBodyLength"/>, since rendering adds markup to a body.
    /// </summary>
    public const int MaxInputLength = 16 * 1024 * 1024;

    /// <summary><c>Nokogiri::HTML5::Document#fragment(html)</c>: a new fragment parsed in a body context.</summary>
    public static HtmlNode ParseFragment( string html )
    {
        var fragment = HtmlNode.CreateFragment( );
        foreach ( var node in ParseNodes( html, ParseContext.Body ) )
        {
            fragment.Append( node );
        }

        return fragment;
    }

    /// <summary>
    /// Parses <paramref name="html"/> in <paramref name="context"/> and returns the top-level nodes,
    /// detached. Raises Nokogiri's <c>ArgumentError</c> for markup deeper than
    /// <see cref="MaxTreeDepth"/> or a tag with more than <see cref="MaxAttributes"/> attributes; the
    /// depth limit also bounds the tree builder's work per token.
    /// </summary>
    public static List<HtmlNode> ParseNodes( string html, ParseContext context )
    {
        ArgumentNullException.ThrowIfNull( html );

        if ( html.Length > MaxInputLength )
        {
            throw new RichTextParseException( RichTextParseError.InputTooLong );
        }

        return GumboTreeBuilder.ParseFragment( NormalizeNewlines( html ), context, MaxTreeDepth, MaxAttributes );
    }

    /// <summary>
    /// The input stream preprocessing every HTML parser applies (CR LF and lone CR become LF), done up
    /// front so that the tokenizer's positions index the string it was given.
    /// </summary>
    private static string NormalizeNewlines( string html ) =>
        html.Contains( '\r' ) ? html.Replace( "\r\n", "\n", StringComparison.Ordinal ).Replace( '\r', '\n' ) : html;
}
