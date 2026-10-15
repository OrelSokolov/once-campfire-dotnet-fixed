using System.Globalization;
using System.Text;

namespace Campfire.Web.Tests.Views.Support;

/// <summary>
/// The DOM normalization the Rust views-A parity tests used (<c>crates/views/tests/support/dom.rs</c> at
/// <c>3fe9f61</c>): a document's token stream after sorting attributes, merging adjacent text, collapsing
/// whitespace runs to one space, and masking per-request CSRF tokens. Whitespace-only text is kept (as one space)
/// because it can affect inline layout. Like html5ever's standalone tokenizer, raw-text elements aren't special.
/// </summary>
internal static class DomTokens
{
    public const string Masked = "[MASKED]";

    public static IReadOnlyList<string> Normalize( string html )
    {
        ArgumentNullException.ThrowIfNull( html );

        var tokens = new List<string>( );
        var text = new StringBuilder( );
        var index = 0;
        while ( index < html.Length )
        {
            if ( html[index] == '<' && TryMarkup( html, ref index, tokens, text ) )
            {
                continue;
            }

            var next = html.IndexOf( '<', index + 1 );
            var end = next < 0 ? html.Length : next;
            text.Append( html, index, end - index );
            index = end;
        }

        FlushText( tokens, text );
        return tokens;
    }

    /// <summary>A readable report of the first difference between two token streams, or <see langword="null"/>.</summary>
    public static string? Diff( IReadOnlyList<string> expected, IReadOnlyList<string> actual )
    {
        if ( expected.SequenceEqual( actual ) )
        {
            return null;
        }

        var first = 0;
        while ( first < expected.Count && first < actual.Count && expected[first] == actual[first] )
        {
            first++;
        }

        var report = new StringBuilder( );
        report.Append( CultureInfo.InvariantCulture, $"first difference at token {first} (expected {expected.Count} tokens, got {actual.Count})\n" );
        for ( var index = Math.Max( 0, first - 4 ); index < first + 6; index++ )
        {
            var expectedToken = index < expected.Count ? expected[index] : "<end>";
            var actualToken = index < actual.Count ? actual[index] : "<end>";
            var marker = expectedToken == actualToken ? " " : "!";
            report.Append( CultureInfo.InvariantCulture, $"{marker} rails: {expectedToken}\n{marker} dotnet: {actualToken}\n" );
        }

        return report.ToString( );
    }

    private static bool TryMarkup( string html, ref int index, List<string> tokens, StringBuilder text )
    {
        if ( html.AsSpan( index ).StartsWith( "<!--" ) )
        {
            FlushText( tokens, text );
            var end = html.IndexOf( "-->", index + 4, StringComparison.Ordinal );
            index = end < 0 ? html.Length : end + 3;
            return true;
        }

        if ( html.AsSpan( index ).StartsWith( "<!" ) )
        {
            FlushText( tokens, text );
            var end = html.IndexOf( '>', index );
            var body = html[(index + 2)..(end < 0 ? html.Length : end)].Trim( );
            var name = body.Split( ' ', 2 ).ElementAtOrDefault( 1 )?.ToLowerInvariant( );
            tokens.Add( $"<!DOCTYPE {name}>" );
            index = end < 0 ? html.Length : end + 1;
            return true;
        }

        if ( index + 1 < html.Length && html[index + 1] == '/' && index + 2 < html.Length && char.IsAsciiLetter( html[index + 2] ) )
        {
            FlushText( tokens, text );
            var end = html.IndexOf( '>', index );
            var name = html[(index + 2)..(end < 0 ? html.Length : end)].Split( [' ', '\t', '\n', '\r', '\f', '/'], 2 )[0].ToLowerInvariant( );
            tokens.Add( $"</{name}>" );
            index = end < 0 ? html.Length : end + 1;
            return true;
        }

        if ( index + 1 < html.Length && char.IsAsciiLetter( html[index + 1] ) )
        {
            FlushText( tokens, text );
            var (name, attributes, next) = HtmlScanner.StartTag( html, index );
            Mask( name, attributes );
            var rendered = string.Concat( attributes.OrderBy( pair => pair.Key, StringComparer.Ordinal ).ThenBy( pair => pair.Value, StringComparer.Ordinal )
                .Select( pair => $" {pair.Key}=\"{EscapeQuoted( pair.Value )}\"" ) );
            tokens.Add( $"<{name}{rendered}>" );
            index = next;
            return true;
        }

        return false;
    }

    /// <summary>CSRF tokens are random per request (and per form): compare them as present, not by value.</summary>
    private static void Mask( string tag, List<KeyValuePair<string, string>> attributes )
    {
        bool Named( string value ) => attributes.Any( pair => pair.Key == "name" && pair.Value == value );

        var maskAttribute = tag == "input" && Named( "authenticity_token" ) ? "value" : tag == "meta" && Named( "csrf-token" ) ? "content" : null;
        if ( maskAttribute is null )
        {
            return;
        }

        for ( var index = 0; index < attributes.Count; index++ )
        {
            if ( attributes[index].Key == maskAttribute )
            {
                attributes[index] = new( maskAttribute, Masked );
            }
        }
    }

    /// <summary>An attribute value as the Rust token stream quotes it: backslashes and double quotes escaped.</summary>
    private static string EscapeQuoted( string value ) =>
        value.Replace( "\\", "\\\\", StringComparison.Ordinal ).Replace( "\"", "\\\"", StringComparison.Ordinal );

    private static void FlushText( List<string> tokens, StringBuilder text )
    {
        if ( text.Length == 0 )
        {
            return;
        }

        tokens.Add( "#" + HtmlScanner.CollapseWhitespace( HtmlScanner.Decode( text.ToString( ) ) ) );
        text.Clear( );
    }
}
