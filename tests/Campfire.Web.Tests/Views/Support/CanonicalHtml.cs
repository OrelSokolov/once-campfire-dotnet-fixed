using System.Text;

namespace Campfire.Web.Tests.Views.Support;

/// <summary>
/// The canonical token stream the views-B goldens hold (<c>reference-tools/views/b/canonical.rb</c>): one string per
/// start tag (attributes in source order), end tag and text run (whitespace collapsed), authenticity tokens masked
/// as «csrf». This builds the same stream from our (well-formed) output, as Rust's
/// <c>crates/views/tests/messages_support/mod.rs</c> did at <c>3fe9f61</c>.
/// </summary>
internal static class CanonicalHtml
{
    public const string Csrf = "«csrf»";

    private static readonly HashSet<string> Void = new( StringComparer.Ordinal )
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "source", "track", "wbr",
    };

    private static readonly HashSet<string> RawText = new( StringComparer.Ordinal ) { "script", "style", "textarea", "title" };

    public static List<string> Tokens( string html )
    {
        var output = new List<string>( );
        var text = new StringBuilder( );

        // Open elements, so that an end tag closes any elements left open inside it, as the HTML parser does
        // (Rails' blockless form_with leaves its form open).
        var open = new List<string>( );
        var index = 0;
        while ( index < html.Length )
        {
            if ( html[index] == '<' && TryMarkup( html, ref index, output, text, open ) )
            {
                continue;
            }

            var next = html.IndexOf( '<', index + 1 );
            var end = next < 0 ? html.Length : next;
            text.Append( html, index, end - index );
            index = end;
        }

        Flush( text, output );
        for ( var depth = open.Count - 1; depth >= 0; depth-- )
        {
            output.Add( $"</{open[depth]}>" );
        }

        output.RemoveAll( token => token == "#" );
        return output;
    }

    public static List<string> TrimWhitespace( List<string> tokens )
    {
        var result = new List<string>( tokens );
        while ( result.Count > 0 && result[0] == "# " )
        {
            result.RemoveAt( 0 );
        }

        while ( result.Count > 0 && result[^1] == "# " )
        {
            result.RemoveAt( result.Count - 1 );
        }

        return result;
    }

    private static bool TryMarkup( string html, ref int index, List<string> output, StringBuilder text, List<string> open )
    {
        if ( html.AsSpan( index ).StartsWith( "<!" ) )
        {
            Flush( text, output );
            var terminator = html.AsSpan( index ).StartsWith( "<!--" ) ? "-->" : ">";
            var end = html.IndexOf( terminator, index, StringComparison.Ordinal );
            index = end < 0 ? html.Length : end + terminator.Length;
            return true;
        }

        if ( html.AsSpan( index ).StartsWith( "</" ) )
        {
            Flush( text, output );
            var end = html.IndexOf( '>', index );
            var name = html[(index + 2)..end].Split( [' ', '\t', '\n'], 2 )[0].ToLowerInvariant( );
            var depth = open.LastIndexOf( name );
            if ( depth >= 0 )
            {
                for ( var closed = open.Count - 1; closed >= depth; closed-- )
                {
                    output.Add( $"</{open[closed]}>" );
                }

                open.RemoveRange( depth, open.Count - depth );
            }

            index = end + 1;
            return true;
        }

        if ( index + 1 < html.Length && char.IsAsciiLetter( html[index + 1] ) )
        {
            Flush( text, output );
            var (name, attributes, next) = HtmlScanner.StartTag( html, index );
            output.Add( StartToken( name, attributes ) );
            index = next;
            if ( !Void.Contains( name ) )
            {
                open.Add( name );
            }

            if ( RawText.Contains( name ) )
            {
                var stop = html.IndexOf( "</" + name, index, StringComparison.OrdinalIgnoreCase );
                stop = stop < 0 ? html.Length : stop;
                var raw = html[index..stop];
                if ( raw.Length > 0 )
                {
                    Push( output, name is "script" or "style" ? raw : HtmlScanner.Decode( raw ) );
                }

                index = stop;
            }

            return true;
        }

        return false;
    }

    private static string StartToken( string name, List<KeyValuePair<string, string>> attributes )
    {
        bool Named( string value ) => attributes.Any( pair => pair.Key == "name" && pair.Value == value );

        var builder = new StringBuilder( ).Append( '<' ).Append( name );
        foreach ( var (key, value) in attributes )
        {
            var masked = (name == "input" && Named( "authenticity_token" ) && key == "value") || (name == "meta" && Named( "csrf-token" ) && key == "content");
            var shown = masked ? Csrf : value;
            builder.Append( ' ' ).Append( key ).Append( "=\"" )
                .Append( shown
                    .Replace( "&", "&amp;", StringComparison.Ordinal )
                    .Replace( "\"", "&quot;", StringComparison.Ordinal )
                    .Replace( "<", "&lt;", StringComparison.Ordinal ) )
                .Append( '"' );
        }

        return builder.Append( '>' ).ToString( );
    }

    private static void Flush( StringBuilder text, List<string> output )
    {
        if ( text.Length > 0 )
        {
            Push( output, HtmlScanner.Decode( text.ToString( ) ) );
            text.Clear( );
        }
    }

    private static void Push( List<string> output, string text )
    {
        var collapsed = HtmlScanner.CollapseWhitespace( text );
        if ( output.Count > 0 && output[^1].StartsWith( '#' ) )
        {
            output[^1] = "#" + HtmlScanner.CollapseWhitespace( output[^1][1..] + collapsed );
        }
        else
        {
            output.Add( "#" + collapsed );
        }
    }
}
