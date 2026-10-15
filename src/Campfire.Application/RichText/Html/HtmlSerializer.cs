using System.Text;

namespace Campfire.Application.RichText.Html;

/// <summary>How attribute values are escaped when serializing.</summary>
internal enum AttributeEscaping
{
    /// <summary>Nokogiri's: <c>&amp;</c>, no-break space and <c>"</c>; <c>&lt;</c> and <c>&gt;</c> stay raw.</summary>
    Nokogiri,

    /// <summary>
    /// Nokogiri's, plus <c>&lt;</c> and <c>&gt;</c> (as the current HTML serialization algorithm
    /// does), so that regular expressions over the markup can't mistake an attribute value for text.
    /// </summary>
    Brackets,
}

/// <summary>
/// Nokogiri's <c>html_standard_serialize</c> (<c>Nokogiri::HTML5::Node#write_to</c>), which is what
/// <c>ActionText::HtmlConversion.node_to_html</c> and Loofah's <c>to_html</c> produce for HTML5
/// documents. Serialization walks the tree without recursion.
/// </summary>
internal static class HtmlSerializer
{
    /// <summary><c>node.to_html</c>: the node itself, or only its children for a fragment.</summary>
    public static string ToHtml( HtmlNode node, AttributeEscaping escaping = AttributeEscaping.Nokogiri )
    {
        ArgumentNullException.ThrowIfNull( node );

        var output = new StringBuilder( );
        if ( node.Kind == HtmlNodeKind.Fragment )
        {
            WriteChildren( node, escaping, output );
        }
        else
        {
            Write( node, escaping, output );
        }

        return output.ToString( );
    }

    /// <summary><c>node.inner_html</c>: the node's children.</summary>
    public static string InnerHtml( HtmlNode node )
    {
        ArgumentNullException.ThrowIfNull( node );

        var output = new StringBuilder( );
        WriteChildren( node, AttributeEscaping.Nokogiri, output );

        return output.ToString( );
    }

    public static bool IsVoidElement( string localName ) => localName is
        "area" or "base" or "basefont" or "bgsound" or "br" or "col" or "embed" or "frame" or "hr" or "img" or "input"
        or "keygen" or "link" or "meta" or "param" or "source" or "track" or "wbr";

    private static bool IsRawTextElement( string localName ) => localName is
        "style" or "script" or "xmp" or "iframe" or "noembed" or "noframes" or "plaintext" or "noscript";

    private static void WriteChildren( HtmlNode node, AttributeEscaping escaping, StringBuilder output )
    {
        foreach ( var child in node.Children )
        {
            Write( child, escaping, output );
        }
    }

    /// <summary>Writes a subtree: an explicit stack of pending nodes and closing tags stands in for recursion.</summary>
    private static void Write( HtmlNode root, AttributeEscaping escaping, StringBuilder output )
    {
        var pending = new Stack<(HtmlNode Node, bool Closing)>( );
        pending.Push( (root, false) );
        while ( pending.TryPop( out var item ) )
        {
            var node = item.Node;
            if ( item.Closing )
            {
                output.Append( "</" ).Append( node.LocalName ).Append( '>' );
                continue;
            }

            switch ( node.Kind )
            {
                case HtmlNodeKind.Element:
                    WriteStartTag( node, escaping, output );
                    if ( node.Namespace != HtmlNamespace.Html || !IsVoidElement( node.LocalName ) )
                    {
                        pending.Push( (node, true) );
                        PushChildrenReversed( pending, node );
                    }

                    break;
                case HtmlNodeKind.Text:
                    WriteText( node, output );
                    break;
                case HtmlNodeKind.Comment:
                    output.Append( "<!--" ).Append( node.Text ).Append( "-->" );
                    break;
                case HtmlNodeKind.CData:
                    output.Append( "<![CDATA[" ).Append( node.Text ).Append( "]]>" );
                    break;
                default:
                    PushChildrenReversed( pending, node );
                    break;
            }
        }
    }

    private static void PushChildrenReversed( Stack<(HtmlNode Node, bool Closing)> pending, HtmlNode node )
    {
        for ( var index = node.Children.Count - 1; index >= 0; index-- )
        {
            pending.Push( (node.Children[index], false) );
        }
    }

    /// <summary>
    /// Elements in the HTML, SVG and MathML namespaces serialize with their local name; the tree
    /// builder creates no others.
    /// </summary>
    private static void WriteStartTag( HtmlNode node, AttributeEscaping escaping, StringBuilder output )
    {
        output.Append( '<' ).Append( node.LocalName );
        foreach ( var attribute in node.Attributes )
        {
            output.Append( ' ' ).Append( attribute.QualifiedName ).Append( "=\"" );
            EscapeAttribute( attribute.Value, escaping, output );
            output.Append( '"' );
        }

        output.Append( '>' );
    }

    private static void WriteText( HtmlNode node, StringBuilder output )
    {
        if ( node.Parent is { IsHtmlElement: true } parent && IsRawTextElement( parent.LocalName ) )
        {
            output.Append( node.Text );
        }
        else
        {
            EscapeText( node.Text, output );
        }
    }

    private static void EscapeAttribute( string value, AttributeEscaping escaping, StringBuilder output )
    {
        foreach ( var character in value )
        {
            _ = character switch
            {
                '&' => output.Append( "&amp;" ),
                '\u00A0' => output.Append( "&nbsp;" ),
                '"' => output.Append( "&quot;" ),
                '<' when escaping == AttributeEscaping.Brackets => output.Append( "&lt;" ),
                '>' when escaping == AttributeEscaping.Brackets => output.Append( "&gt;" ),
                _ => output.Append( character ),
            };
        }
    }

    private static void EscapeText( string value, StringBuilder output )
    {
        foreach ( var character in value )
        {
            _ = character switch
            {
                '&' => output.Append( "&amp;" ),
                '\u00A0' => output.Append( "&nbsp;" ),
                '<' => output.Append( "&lt;" ),
                '>' => output.Append( "&gt;" ),
                _ => output.Append( character ),
            };
        }
    }
}
