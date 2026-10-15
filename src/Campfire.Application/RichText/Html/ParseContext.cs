namespace Campfire.Application.RichText.Html;

/// <summary>
/// The context element of a fragment parse, as Nokogiri passes it to Gumbo: <c>Document#fragment</c>
/// parses in a <c>body</c>, and <c>Node#fragment</c> (behind <c>replace</c> and <c>inner_html=</c>) in
/// the node itself, with its namespace, the <c>encoding</c> of a MathML <c>annotation-xml</c>, and
/// whether it is or sits inside an HTML <c>form</c>.
/// </summary>
internal readonly record struct ParseContext( HtmlNamespace Namespace, string LocalName, string? Encoding, bool HasFormAncestor )
{
    public static ParseContext Body { get; } = new( HtmlNamespace.Html, "body", null, false );

    /// <summary>
    /// The context Nokogiri uses for markup parsed for <paramref name="node"/>: the node when it is
    /// an element; Gumbo falls back to a body for anything else (a fragment's root).
    /// </summary>
    public static ParseContext For( HtmlNode? node )
    {
        if ( node is not { IsElement: true } )
        {
            return Body;
        }

        var encoding = node is { Namespace: HtmlNamespace.MathMl, LocalName: "annotation-xml" } ? node.GetAttribute( "encoding" ) : null;
        var hasFormAncestor = node.IsNamedHtmlForm( ) || node.Ancestors( ).Any( ancestor => ancestor.IsNamedHtmlForm( ) );

        return new ParseContext( node.Namespace, node.LocalName, encoding, hasFormAncestor );
    }

    /// <summary>Gumbo's <c>create_fragment_ctx_element</c>.</summary>
    public HtmlNode CreateElement( )
    {
        var attributes = Encoding is null ? null : new List<HtmlAttribute> { new( null, "encoding", Encoding ) };

        return HtmlNode.CreateElement( Namespace, LocalName, attributes );
    }
}
