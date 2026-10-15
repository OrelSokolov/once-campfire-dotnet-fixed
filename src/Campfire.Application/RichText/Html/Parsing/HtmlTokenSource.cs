using AngleSharp.Html;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using AngleSharp.Text;

namespace Campfire.Application.RichText.Html.Parsing;

/// <summary>
/// Tokens from AngleSharp's HTML tokenizer (the inert, spec-following half of its parser), shaped
/// the way Gumbo's tokenizer emits them to its tree builder.
/// </summary>
/// <remarks>
/// Two differences between the tokenizers are bridged here. AngleSharp drops U+0000 in the data
/// state, where Gumbo emits a NULL token (ignored in HTML content, a U+FFFD in SVG and MathML), so
/// each U+0000 is swapped for a noncharacter absent from the input before tokenizing and swapped
/// back as the token requires. And AngleSharp reports a CDATA section as ordinary characters, where
/// Gumbo emits CDATA tokens (which become a CDATA node), so a character token that starts at a
/// <c>&lt;![CDATA[</c> in the source is one.
/// </remarks>
internal sealed class HtmlTokenSource : IDisposable
{
    private const char ReplacementCharacter = '\uFFFD';

    private readonly string _source;
    private readonly HtmlTokenizer _tokenizer;
    private readonly char? _nullStandIn;
    private readonly int _maxAttributes;
    private readonly Queue<ParserToken> _pending = new( );

    public HtmlTokenSource( string html, int maxAttributes )
    {
        ArgumentNullException.ThrowIfNull( html );

        _maxAttributes = maxAttributes;
        _nullStandIn = html.Contains( '\0' ) ? FindNullStandIn( html ) : null;
        _source = _nullStandIn is { } standIn ? html.Replace( '\0', standIn ) : html;
        _tokenizer = new HtmlTokenizer( new TextSource( _source ), HtmlEntityProvider.Resolver );
    }

    public void Dispose( ) => _tokenizer.Dispose( );

    /// <summary>The tokenizer state for the next token (RCDATA, RAWTEXT, script data, PLAINTEXT).</summary>
    public HtmlParseMode State
    {
        get => _tokenizer.State;
        set => _tokenizer.State = value;
    }

    /// <summary>The next token, lexed with CDATA sections allowed when the adjusted current node is foreign.</summary>
    public ParserToken Next( bool acceptCData )
    {
        while ( _pending.Count == 0 )
        {
            var inData = _tokenizer.State == HtmlParseMode.PCData;
            _tokenizer.IsAcceptingCharacterData = acceptCData;
            Enqueue( _tokenizer.Get( ), inData );
        }

        return _pending.Dequeue( );
    }

    /// <summary>
    /// Whether Gumbo's tokenizer rejected the last tag: once a tag holds <c>max_attributes</c>
    /// distinct names, any further attribute, even a duplicate it would drop, exceeds the limit.
    /// </summary>
    public bool LastTagExceededAttributeLimit { get; private set; }

    private void Enqueue( HtmlToken token, bool inData )
    {
        switch ( token.Type )
        {
            case HtmlTokenType.Character:
                EnqueueCharacters( token, inData );
                break;
            case HtmlTokenType.StartTag or HtmlTokenType.EndTag:
                _pending.Enqueue( ConvertTag( ( HtmlTagToken ) token ) );
                break;
            case HtmlTokenType.Comment:
                _pending.Enqueue( new ParserToken( ParserTokenKind.Comment, text: RestoreNulls( token.Data ) ) );
                break;
            case HtmlTokenType.Doctype:
                _pending.Enqueue( new ParserToken( ParserTokenKind.Doctype ) );
                break;
            default:
                _pending.Enqueue( ParserToken.EndOfFile );
                break;
        }
    }

    /// <summary>
    /// Gumbo's <c>finish_attribute_name</c>: past the limit any attribute is an error; otherwise the
    /// first of each name is kept. AngleSharp already keeps only the first of each name in a start tag
    /// (an end tag keeps them all), but compares names before a U+0000 becomes U+FFFD.
    /// </summary>
    private ParserToken ConvertTag( HtmlTagToken tag )
    {
        var attributes = new List<HtmlAttribute>( tag.Attributes.Count );
        var names = new HashSet<string>( StringComparer.Ordinal );
        var exceeded = false;
        foreach ( var attribute in tag.Attributes )
        {
            if ( names.Count >= _maxAttributes )
            {
                exceeded = true;
                break;
            }

            var name = RestoreNulls( attribute.Name );
            if ( names.Add( name ) )
            {
                attributes.Add( new HtmlAttribute( null, name, RestoreNulls( attribute.Value ) ) );
            }
        }

        var kind = tag.Type == HtmlTokenType.StartTag ? ParserTokenKind.StartTag : ParserTokenKind.EndTag;

        // A start tag's last attribute reached the limit; only its source shows a dropped duplicate after it
        LastTagExceededAttributeLimit = exceeded
            || (kind == ParserTokenKind.StartTag && names.Count == _maxAttributes && tag.Attributes is [.., var last] && HasAttributeAfter( last.Position ));

        return new ParserToken( kind, RestoreNulls( tag.Name ) ) { Attributes = attributes, IsSelfClosing = tag.IsSelfClosing };
    }

    /// <summary>
    /// Whether another attribute follows the one whose name starts at <paramref name="position"/> in
    /// the tag just lexed. The rest of the tag is lexed again as an end tag, whose duplicate attributes
    /// AngleSharp keeps.
    /// </summary>
    private bool HasAttributeAfter( TextPosition position )
    {
        var start = position.Position - 1;
        var rest = string.Concat( "</x ", _source.AsSpan( start, _tokenizer.Position - start ), ">" );
        using var tokenizer = new HtmlTokenizer( new TextSource( rest ), HtmlEntityProvider.Resolver );

        return tokenizer.Get( ) is HtmlTagToken { Attributes.Count: > 1 };
    }

    /// <summary>Splits character data into runs of whitespace, NULs and other characters.</summary>
    private void EnqueueCharacters( HtmlToken token, bool inData )
    {
        var text = token.Data;
        if ( text.Length == 0 )
        {
            return;
        }

        if ( inData && IsCDataSectionAt( token.Position.Position - 1 ) )
        {
            EnqueueCData( text );
            return;
        }

        var start = 0;
        while ( start < text.Length )
        {
            var kind = KindOf( text[start], inData );
            var end = start + 1;
            while ( end < text.Length && KindOf( text[end], inData ) == kind )
            {
                end++;
            }

            var run = text[start..end];
            _pending.Enqueue( new ParserToken( kind, text: kind == ParserTokenKind.Null ? run : RestoreNulls( run ) ) );
            start = end;
        }
    }

    /// <summary>Every character of a CDATA section is a CDATA token, except U+0000, which is a NULL token.</summary>
    private void EnqueueCData( string text )
    {
        var start = 0;
        while ( start < text.Length )
        {
            var isNull = text[start] == _nullStandIn;
            var end = start + 1;
            while ( end < text.Length && (text[end] == _nullStandIn) == isNull )
            {
                end++;
            }

            _pending.Enqueue( new ParserToken( isNull ? ParserTokenKind.Null : ParserTokenKind.CData, text: text[start..end] ) );
            start = end;
        }
    }

    private bool IsCDataSectionAt( int index ) =>
        index >= 0 && _source.AsSpan( index ).StartsWith( "<![CDATA[", StringComparison.Ordinal );

    private ParserTokenKind KindOf( char character, bool inData )
    {
        if ( character == _nullStandIn && inData )
        {
            return ParserTokenKind.Null;
        }

        return character is '\t' or '\n' or '\f' or '\r' or ' ' ? ParserTokenKind.Whitespace : ParserTokenKind.Character;
    }

    /// <summary>Outside the data state (and in names, values and comments) a U+0000 is a U+FFFD.</summary>
    private string RestoreNulls( string value ) =>
        _nullStandIn is { } standIn && value.Contains( standIn ) ? value.Replace( standIn, ReplacementCharacter ) : value;

    /// <summary>
    /// A noncharacter (U+FDD0 to U+FDEF) the input doesn't contain. When the input contains all of
    /// them, U+0000 is left to AngleSharp, which drops it in the data state and replaces it elsewhere.
    /// </summary>
    private static char? FindNullStandIn( string html )
    {
        for ( var candidate = '\uFDD0'; candidate <= '\uFDEF'; candidate++ )
        {
            if ( !html.Contains( candidate ) )
            {
                return candidate;
            }
        }

        return null;
    }
}
