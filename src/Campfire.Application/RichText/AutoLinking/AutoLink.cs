using System.Text;
using System.Text.RegularExpressions;
using Campfire.Application.RichText.Html;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.AutoLinking;

/// <summary>
/// rails_autolink 1.1.8's <c>auto_link(text, html: { target: "_blank" }, sanitize_options: ...)</c>,
/// as <c>MessagesHelper#message_presentation</c> calls it. It works on the serialized HTML with
/// regular expressions, so the exact serialization from the earlier steps matters.
/// </summary>
/// <remarks>
/// One deliberate difference closes a stored XSS in rails_autolink (security fix: "autolink attribute
/// breakout", decided fix): the sanitized HTML is serialized with <c>&lt;</c> and <c>&gt;</c> escaped in
/// attribute values. Nokogiri leaves them raw, so a URL after a <c>&gt;</c> in a <c>title</c> looked like
/// text to <c>auto_linked?</c>, and the <c>&lt;a href="..."&gt;</c> inserted there closed the attribute and
/// turned the rest of its value into markup. With them escaped, every <c>&lt;</c> and <c>&gt;</c> in the
/// text belongs to a tag, so links are only ever inserted between tags.
/// </remarks>
internal static partial class AutoLink
{
    /// <summary>The schemes <c>AUTO_LINK_RE</c> links.</summary>
    private const string AutoLinkSchemes =
        "ed2k|ftp|http|https|irc|mailto|news|gopher|nntp|telnet|webcal|xmpp|callto|feed|svn|urn|aim|rsync|tag|ssh|sftp|rtsp|afs|file";

    /// <summary><c>auto_link(html, html: { target: "_blank" }, sanitize_options: { tags:, attributes: })</c>.</summary>
    public static string Apply( string text, SafeList sanitizeOptions )
    {
        ArgumentNullException.ThrowIfNull( text );
        ArgumentNullException.ThrowIfNull( sanitizeOptions );

        if ( RubyStrings.IsBlank( text ) )
        {
            return "";
        }

        var sanitized = SafeListSanitizer.Sanitize( text, sanitizeOptions, AttributeEscaping.Brackets );

        return LinkEmailAddresses( LinkUrls( sanitized ) );
    }

    private static string LinkUrls( string text )
    {
        var tags = new AutoLinkTagIndex( text );
        var output = new StringBuilder( text.Length );
        var copied = 0;
        foreach ( Match match in AutoLinkPattern( ).Matches( text ) )
        {
            output.Append( text, copied, match.Index - copied );
            copied = match.Index + match.Length;
            if ( tags.IsAutoLinked( match.Index, match.Index + match.Length ) )
            {
                output.Append( match.Value );
            }
            else
            {
                AppendUrlLink( output, match.Value, hasScheme: match.Groups[1].Success );
            }
        }

        return output.Append( text, copied, text.Length - copied ).ToString( );
    }

    /// <summary>One link: trailing punctuation (and a trailing <c>&amp;gt;</c>) stays outside it.</summary>
    private static void AppendUrlLink( StringBuilder output, string url, bool hasScheme )
    {
        var href = url;
        var punctuation = new List<string>( );
        var brackets = new BracketCounts( href );
        while ( href.Length > 0 && LastCharacter( href ) is var last && !IsUrlEndCharacter( last ) )
        {
            href = href[..^last.Length];
            punctuation.Add( last );
            brackets.Remove( last );
            if ( OpeningBracket( last ) is { } opening && brackets.Count( opening ) > brackets.Count( last ) )
            {
                href += last;
                punctuation.RemoveAt( punctuation.Count - 1 );
                break;
            }
        }

        var trailingGreaterThan = "";
        if ( href.EndsWith( "&gt;", StringComparison.Ordinal ) )
        {
            href = href[..^4];
            trailingGreaterThan = "&gt;";
        }

        var linkText = SafeListSanitizer.Sanitize( href, SafeList.Defaults );
        var linkHref = SafeListSanitizer.Sanitize( hasScheme ? href : "http://" + href, SafeList.Defaults );

        // content_tag(:a, link_text, attrs, false): nothing escaped but double quotes in attributes
        output.Append( "<a target=\"_blank\" href=\"" )
            .Append( linkHref.Replace( "\"", "&quot;", StringComparison.Ordinal ) )
            .Append( "\">" )
            .Append( linkText )
            .Append( "</a>" );

        // SafeBuffer#+ escapes the (unsafe) punctuation string
        punctuation.Reverse( );
        output.Append( RubyStrings.HtmlEscape( string.Concat( punctuation ) ) ).Append( trailingGreaterThan );
    }

    /// <summary>
    /// <c>auto_link_email_addresses</c>. <c>AUTO_EMAIL_RE</c> starts with a lookbehind that no local-part
    /// character precedes the address; that is checked here, and the rest is matched at each position.
    /// </summary>
    private static string LinkEmailAddresses( string text )
    {
        var tags = new AutoLinkTagIndex( text );
        var output = new StringBuilder( text.Length );
        var copied = 0;
        var position = 0;
        while ( position < text.Length )
        {
            var precededByLocalCharacter = position > 0 && IsEmailLocalCharacter( text[position - 1] );
            var match = precededByLocalCharacter ? null : EmailPattern( ).Match( text, position );
            if ( match is null || !match.Success )
            {
                position++;
                continue;
            }

            var (start, end) = (match.Index, match.Index + match.Length);
            output.Append( text, copied, start - copied );
            if ( tags.IsAutoLinked( start, end ) )
            {
                output.Append( match.Value );
            }
            else
            {
                AppendEmailLink( output, match.Value );
            }

            copied = end;
            position = Math.Max( end, position + 1 );
        }

        return output.Append( text, copied, text.Length - copied ).ToString( );
    }

    /// <summary><c>mail_to text, display_text, html_options</c> after sanitizing the address.</summary>
    private static void AppendEmailLink( StringBuilder output, string email )
    {
        var sanitized = SafeListSanitizer.Sanitize( email, SafeList.Defaults );

        // The display text is only sanitized (and so marked safe) when sanitizing changed the address
        var display = sanitized == email ? RubyStrings.HtmlEscape( email ) : SafeListSanitizer.Sanitize( email, SafeList.Defaults );
        var href = "mailto:" + RubyStrings.UrlEncode( sanitized ).Replace( "%40", "@", StringComparison.Ordinal );
        output.Append( "<a target=\"_blank\" href=\"" )
            .Append( RubyStrings.HtmlEscape( href ) )
            .Append( "\">" )
            .Append( display )
            .Append( "</a>" );
    }

    /// <summary><c>AUTO_EMAIL_LOCAL_RE</c>: <c>/[\w.!#\$%&amp;'*\/=?^`{|}~+-]/</c>, with Ruby's ASCII <c>\w</c>.</summary>
    private static bool IsEmailLocalCharacter( char character ) =>
        char.IsAsciiLetterOrDigit( character ) || "_.!#$%&'*/=?^`{|}~+-".Contains( character, StringComparison.Ordinal );

    /// <summary>
    /// A URL may end in a word character (Ruby's <c>\p{Word}</c>), <c>/</c>, <c>-</c>, <c>=</c> or
    /// <c>;</c>; anything else at its end is punctuation around it.
    /// </summary>
    private static bool IsUrlEndCharacter( string character ) =>
        character is "/" or "-" or "=" or ";" || OnigmoWord.Contains( Rune.GetRuneAt( character, 0 ) );

    /// <summary>The last character (a whole surrogate pair when it is one).</summary>
    private static string LastCharacter( string value ) =>
        value.Length >= 2 && char.IsSurrogatePair( value[^2], value[^1] ) ? value[^2..] : value[^1..];

    /// <summary>Brackets whose closing half may end a URL when the URL opened it.</summary>
    private static string? OpeningBracket( string closing ) => closing switch
    {
        "]" => "[",
        ")" => "(",
        "}" => "{",
        _ => null,
    };

    /// <summary>
    /// <c>AUTO_LINK_RE</c>: <c>(?: ((?:ed2k|...|file):)// | www\.\w ) [^\s&lt; "]+</c>, case
    /// insensitive, with Ruby's ASCII <c>\s</c> and <c>\w</c>.
    /// </summary>
    [GeneratedRegex(
        """(?:(?i:((?:""" + AutoLinkSchemes + """):))//|(?i:www)\.[a-zA-Z0-9_])[^ \t\r\n\x0B\x0C<\u00A0"]+""",
        RegexOptions.CultureInvariant )]
    private static partial Regex AutoLinkPattern( );

    /// <summary><c>AUTO_EMAIL_RE</c> without its lookbehind, anchored where the search starts.</summary>
    [GeneratedRegex( @"\G[a-zA-Z0-9_.!#$%+-]\.?[a-zA-Z0-9_.!#$%&'*/=?^`{|}~+-]*@[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)+", RegexOptions.CultureInvariant )]
    private static partial Regex EmailPattern( );

    /// <summary>
    /// How many of each bracket a URL has, kept up to date as trailing punctuation is stripped rather
    /// than recounted for each character stripped, as rails_autolink does.
    /// </summary>
    private sealed class BracketCounts
    {
        private readonly Dictionary<string, int> _counts = new( StringComparer.Ordinal ) { ["["] = 0, ["]"] = 0, ["("] = 0, [")"] = 0, ["{"] = 0, ["}"] = 0 };

        public BracketCounts( string value )
        {
            foreach ( var character in value )
            {
                var key = character.ToString( );
                if ( _counts.TryGetValue( key, out var count ) )
                {
                    _counts[key] = count + 1;
                }
            }
        }

        public int Count( string bracket ) => _counts.GetValueOrDefault( bracket );

        public void Remove( string character )
        {
            if ( _counts.TryGetValue( character, out var count ) )
            {
                _counts[character] = count - 1;
            }
        }
    }
}
