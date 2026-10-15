using Campfire.Application.RichText.Html;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Content;

/// <summary>
/// <c>ActionText::PlainTextConversion.node_to_plain_text</c>: a bottom-up reduction keyed on each
/// node's name, walked without recursion like Action Text's <c>BottomUpReducer</c>.
/// </summary>
internal static class PlainTextConversion
{
    public static string NodeToPlainText( HtmlNode root )
    {
        var values = new Dictionary<HtmlNode, string>( ReferenceEqualityComparer.Instance );
        foreach ( var node in PostOrder( root ) )
        {
            values[node] = PlainTextFor( node, node.Children.Select( child => values[child] ).ToList( ) );
        }

        return RubyStrings.ChompNewlines( values[root] );
    }

    private static List<HtmlNode> PostOrder( HtmlNode root )
    {
        var preOrder = new List<HtmlNode>( );
        var pending = new Stack<HtmlNode>( );
        pending.Push( root );
        while ( pending.TryPop( out var node ) )
        {
            preOrder.Add( node );
            foreach ( var child in node.Children )
            {
                pending.Push( child );
            }
        }

        // Reversing "node, then children right to left" visits children left to right before their parent
        preOrder.Reverse( );

        return preOrder;
    }

    private static string PlainTextFor( HtmlNode node, List<string> childValues ) => node.Name switch
    {
        "script" or "style" or "unsupported" => "",
        "h1" or "p" => Block( childValues ),
        "ul" or "ol" => ListDepth( node ) > 0 ? "\n" + Block( childValues ) : Block( childValues ),
        "br" => "\n",
        // Text nodes, and elements that happen to be named "text" (SVG's), use `node.text`
        "text" => RubyStrings.ChompNewlines( node.TextContent( ) ),
        "div" => RubyStrings.ChompNewlines( string.Concat( childValues ) ) + "\n",
        "figcaption" => $"[{RubyStrings.ChompNewlines( string.Concat( childValues ) )}]",
        "blockquote" => Blockquote( childValues ),
        "li" => ListItem( node, childValues ),
        _ => string.Concat( childValues ),
    };

    private static string Block( List<string> childValues ) => RubyStrings.ChompNewlines( string.Concat( childValues ) ) + "\n\n";

    /// <summary><c>text.insert(text.rindex(/\S/) + 1, "”")</c>, then <c>text.index(/\S/)</c> for "“" (Ruby's <c>\S</c> is ASCII).</summary>
    private static string Blockquote( List<string> childValues )
    {
        var text = Block( childValues );
        if ( RubyStrings.IsBlank( text ) )
        {
            return "“”";
        }

        var last = text.Length - 1;
        while ( IsAsciiSpace( text[last] ) )
        {
            last--;
        }

        text = text.Insert( last + 1, "”" );
        var first = 0;
        while ( IsAsciiSpace( text[first] ) )
        {
            first++;
        }

        return text.Insert( first, "“" );
    }

    private static bool IsAsciiSpace( char character ) => character is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private static string ListItem( HtmlNode node, List<string> childValues )
    {
        var text = RubyStrings.ChompNewlines( string.Concat( childValues ) );
        var depth = ListDepth( node );
        var indentation = depth > 1 ? string.Concat( Enumerable.Repeat( "  ", depth - 1 ) ) : "";

        return $"{indentation}{BulletFor( node )} {text}\n";
    }

    private static string BulletFor( HtmlNode node )
    {
        var list = node.Ancestors( ).FirstOrDefault( ancestor => IsList( ancestor.Name ) );
        if ( list?.Name != "ol" )
        {
            return "•";
        }

        var index = node.Parent is { } parent ? parent.ElementChildren( ).IndexOf( node ) : 0;

        return $"{index + 1}.";
    }

    private static int ListDepth( HtmlNode node ) => node.Ancestors( ).Count( ancestor => IsList( ancestor.Name ) );

    private static bool IsList( string name ) => name is "ul" or "ol";
}
