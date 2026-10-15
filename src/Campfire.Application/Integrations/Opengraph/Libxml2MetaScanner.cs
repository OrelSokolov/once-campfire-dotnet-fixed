using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary>
/// The slice of <c>Nokogiri::HTML(html)</c> (libxml2's legacy HTML parser, in recovery mode) that
/// <c>Opengraph::Document</c> reads: every <c>&lt;meta&gt;</c> element's attributes. A port of the Rust port's
/// scanner as hardened in <c>33d75d6</c> (linear time; a tag keeps its first 256 attributes). Rails parses these pages
/// with libxml2's HTML4 parser, not the HTML5 one rich text uses, so this stays a scanner of its own.
/// </summary>
/// <remarks>
/// The body comes from <c>Opengraph::Fetch</c> tagged UTF-8, so libxml2 decodes it as UTF-8 whatever the page declares,
/// taking a byte that isn't valid UTF-8 as Latin-1. It tokenizes the way the pre-HTML5 parser does:
/// <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> hold raw text, comments and <c>&lt;!…&gt;</c>/<c>&lt;?…&gt;</c>
/// markup are skipped, tag and attribute names are lowercased, the first of a repeated attribute wins, and attribute
/// values decode HTML 4 entities only when terminated by <c>;</c>, and numeric references with or without one (an
/// invalid one cuts the value short, as the NUL it produces ends libxml2's C string). Everything the scanner matches
/// is ASCII.
/// </remarks>
internal static partial class Libxml2MetaScanner
{
    /// <summary>
    /// How many attributes of one tag are kept; the rest are parsed and dropped (see
    /// docs/compatibility.md#deliberate-differences-and-bounds).
    /// </summary>
    public const int MaxAttributes = 256;

    /// <summary>libxml2 reading a UTF-8 buffer: valid sequences decode, and any other byte is taken as Latin-1.</summary>
    public static string Decode( ReadOnlySpan<byte> bytes )
    {
        var output = new StringBuilder( bytes.Length );
        while ( !bytes.IsEmpty )
        {
            if ( Rune.DecodeFromUtf8( bytes, out var rune, out var consumed ) == OperationStatus.Done )
            {
                output.Append( rune );
                bytes = bytes[consumed..];
            }
            else
            {
                output.Append( ( char ) bytes[0] );
                bytes = bytes[1..];
            }
        }

        return output.ToString( );
    }

    /// <summary>The <c>&lt;meta&gt;</c> elements of the document, in document order.</summary>
    public static List<MetaElement> MetaElements( string html )
    {
        // A NUL ends libxml2's input
        var nullCharacterIndex = html.IndexOf( '\0' );
        var scanner = new Scanner( nullCharacterIndex < 0 ? html : html[..nullCharacterIndex] );
        var metas = new List<MetaElement>( );
        while ( scanner.Peek( 0 ) is { } character )
        {
            if ( character != '<' )
            {
                scanner.SkipText( );
                continue;
            }

            switch ( scanner.Peek( 1 ) )
            {
                case '/':
                    scanner.EndTag( );
                    break;
                case '!':
                    scanner.MarkupDeclaration( );
                    break;
                case '?':
                    scanner.SkipPast( '>' );
                    break;
                case { } next when char.IsAsciiLetter( next ):
                    var (name, element, selfClosing) = scanner.StartTag( );
                    if ( name == "meta" )
                    {
                        metas.Add( element );
                    }
                    else if ( name is "script" or "style" && !selfClosing )
                    {
                        scanner.RawText( name );
                    }

                    break;
                default:
                    scanner.Advance( 1 );
                    break;
            }
        }

        return metas;
    }

    /// <summary>
    /// <c>Nokogiri::HTML4::Document#meta_encoding</c>: the first <c>meta[@charset]</c>'s value, else the charset in the
    /// first <c>http-equiv="Content-Type"</c> meta that has a <c>content</c>.
    /// </summary>
    public static string? MetaEncoding( IReadOnlyList<MetaElement> metas )
    {
        if ( metas.FirstOrDefault( static meta => meta.HasAttribute( OpengraphAttributeNames.Charset ) ) is { } withCharset )
        {
            return withCharset.GetAttribute( OpengraphAttributeNames.Charset );
        }

        var contentType = metas.FirstOrDefault( static meta => meta.HasAttribute( OpengraphAttributeNames.Content ) && IsContentTypeEquivalent( meta ) );
        if ( contentType?.GetAttribute( OpengraphAttributeNames.Content ) is not { } content )
        {
            return null;
        }

        var match = CharsetPattern( ).Match( content );
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary><c>http-equiv="Content-Type"</c>, ignoring case.</summary>
    private static bool IsContentTypeEquivalent( MetaElement meta ) =>
        string.Equals( meta.GetAttribute( OpengraphAttributeNames.HttpEquiv ), "content-type", StringComparison.OrdinalIgnoreCase );

    /// <summary><c>content[/charset\s*=\s*([\w-]+)/i, 1]</c>, with Ruby's ASCII <c>\s</c> and <c>\w</c>.</summary>
    [GeneratedRegex(
        @"[Cc][Hh][Aa][Rr][Ss][Ee][Tt][ \t\n\x0B\x0C\r]*=[ \t\n\x0B\x0C\r]*([A-Za-z0-9_-]+)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking )]
    private static partial Regex CharsetPattern( );

    private sealed class Scanner( string text )
    {
        private int _position;

        public char? Peek( int ahead ) => _position + ahead < text.Length ? text[_position + ahead] : null;

        public void Advance( int count ) => _position += count;

        /// <summary>Moves to the next <c>&lt;</c>.</summary>
        public void SkipText( )
        {
            var next = text.IndexOf( '<', _position );
            _position = next < 0 ? text.Length : next;
        }

        /// <summary>Moves past the next <paramref name="delimiter"/>, or to the end.</summary>
        public void SkipPast( char delimiter )
        {
            var next = text.IndexOf( delimiter, _position );
            _position = next < 0 ? text.Length : next + 1;
        }

        public void EndTag( )
        {
            _position += 2;
            if ( HtmlName( ) is not null )
            {
                SkipPast( '>' );
            }
        }

        /// <summary>
        /// <c>&lt;!--…--&gt;</c> (with <c>&lt;!--&gt;</c> and <c>&lt;!---&gt;</c> closing at once, and <c>--!&gt;</c>
        /// accepted), <c>&lt;!DOCTYPE…&gt;</c>, and any other <c>&lt;!…&gt;</c> skipped as a bogus comment.
        /// </summary>
        public void MarkupDeclaration( )
        {
            if ( Peek( 2 ) != '-' || Peek( 3 ) != '-' )
            {
                SkipPast( '>' );
                return;
            }

            _position += 4;
            if ( Peek( 0 ) == '>' )
            {
                _position += 1;
                return;
            }

            if ( Peek( 0 ) == '-' && Peek( 1 ) == '>' )
            {
                _position += 2;
                return;
            }

            var closeIndex = text.IndexOf( "-->", _position, StringComparison.Ordinal );
            var bangCloseIndex = text.IndexOf( "--!>", _position, StringComparison.Ordinal );
            _position = (closeIndex, bangCloseIndex) switch
            {
                ( < 0, < 0 ) => text.Length,
                ( < 0, _ ) => bangCloseIndex + 4,
                (_, < 0 ) => closeIndex + 3,
                _ => closeIndex <= bangCloseIndex ? closeIndex + 3 : bangCloseIndex + 4,
            };
        }

        /// <summary><c>htmlParseStartTag</c>: the name, the element, and whether it ended with <c>/&gt;</c>.</summary>
        public (string Name, MetaElement Element, bool SelfClosing) StartTag( )
        {
            _position += 1;
            var name = HtmlName( ) ?? "";
            var attributes = new List<KeyValuePair<string, string>>( );
            SkipBlanks( );
            while ( Peek( 0 ) is { } character && character != '>' && !(character == '/' && Peek( 1 ) == '>') )
            {
                if ( HtmlName( ) is { } attribute )
                {
                    SkipBlanks( );
                    var value = "";
                    if ( Peek( 0 ) == '=' )
                    {
                        _position += 1;
                        SkipBlanks( );
                        value = AttributeValue( );
                    }

                    if ( attributes.Count < MaxAttributes && attributes.IndexOfKey( attribute ) < 0 )
                    {
                        attributes.Add( new( attribute, value ) );
                    }
                }
                else
                {
                    // Dump the bogus attribute string up to the next blank or the end of the tag
                    while ( Peek( 0 ) is { } bogus && !IsBlank( bogus ) && bogus != '>' && !(bogus == '/' && Peek( 1 ) == '>') )
                    {
                        _position += 1;
                    }
                }

                SkipBlanks( );
            }

            var selfClosing = Peek( 0 ) == '/';
            if ( selfClosing )
            {
                _position += 2;
            }
            else if ( Peek( 0 ) == '>' )
            {
                _position += 1;
            }

            return (name, new MetaElement( attributes ), selfClosing);
        }

        /// <summary><c>htmlParseScript</c>: everything up to <c>&lt;/name</c> (any case) is text.</summary>
        public void RawText( string name )
        {
            var end = $"</{name}";
            while ( _position < text.Length )
            {
                var next = text.IndexOf( '<', _position );
                if ( next < 0 )
                {
                    _position = text.Length;
                    return;
                }

                _position = next;
                if ( text.Length - next >= end.Length && Ascii.EqualsIgnoreCase( text.AsSpan( next, end.Length ), end ) )
                {
                    return;
                }

                _position += 1;
            }
        }

        private static bool IsBlank( char character ) => character is ' ' or '\t' or '\n' or '\r';

        private static bool IsUnquotedValueEnd( char character ) => character == '>' || IsBlank( character );

        private void SkipBlanks( )
        {
            while ( Peek( 0 ) is { } character && IsBlank( character ) )
            {
                _position += 1;
            }
        }

        /// <summary><c>htmlParseHTMLName</c>: <c>[A-Za-z_:.][A-Za-z0-9:_.-]*</c>, lowercased.</summary>
        private string? HtmlName( )
        {
            if ( Peek( 0 ) is not { } first || !(char.IsAsciiLetter( first ) || first is '_' or ':' or '.') )
            {
                return null;
            }

            var start = _position;
            while ( Peek( 0 ) is { } character && (char.IsAsciiLetterOrDigit( character ) || character is ':' or '-' or '_' or '.') )
            {
                _position += 1;
            }

            return text[start.._position].ToLowerInvariant( );
        }

        /// <summary><c>htmlParseAttValue</c>.</summary>
        private string AttributeValue( )
        {
            if ( Peek( 0 ) is not ('"' or '\'') )
            {
                return AttributeText( null );
            }

            var quote = Peek( 0 )!.Value;
            _position += 1;
            var value = AttributeText( quote );
            if ( Peek( 0 ) == quote )
            {
                _position += 1;
            }

            return value;
        }

        /// <summary><c>htmlParseHTMLAttribute</c>: up to the quote, or (unquoted) a blank or <c>&gt;</c>.</summary>
        private string AttributeText( char? stop )
        {
            var output = new StringBuilder( );
            var truncated = false;
            while ( true )
            {
                var start = _position;
                while ( Peek( 0 ) is { } character && character != '&' && character != stop && !(stop is null && IsUnquotedValueEnd( character )) )
                {
                    _position += 1;
                }

                if ( !truncated )
                {
                    output.Append( text, start, _position - start );
                }

                if ( Peek( 0 ) != '&' )
                {
                    return output.ToString( );
                }

                var decoded = Peek( 1 ) == '#' ? CharacterReference( ) : EntityReference( );
                if ( decoded is null )
                {
                    truncated = true;
                }
                else if ( !truncated )
                {
                    output.Append( decoded );
                }
            }
        }

        /// <summary><c>htmlParseCharRef</c>: null for a value that isn't a valid XML character.</summary>
        private string? CharacterReference( )
        {
            var hex = Peek( 2 ) is 'x' or 'X';
            _position += hex ? 3 : 2;
            var radix = hex ? 16 : 10;
            var value = 0;
            while ( Peek( 0 ) is { } character )
            {
                if ( character == ';' )
                {
                    _position += 1;
                    break;
                }

                var digit = character switch
                {
                    >= '0' and <= '9' => character - '0',
                    >= 'a' and <= 'f' when hex => character - 'a' + 10,
                    >= 'A' and <= 'F' when hex => character - 'A' + 10,
                    _ => -1,
                };
                if ( digit < 0 )
                {
                    break;
                }

                if ( value < 0x110000 )
                {
                    value = (value * radix) + digit;
                }

                _position += 1;
            }

            var isCharacter = value is 0x9 or 0xA or 0xD or (>= 0x20 and <= 0xD7FF) or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF);
            return isCharacter ? char.ConvertFromUtf32( value ) : null;
        }

        /// <summary><c>htmlParseEntityRef</c>: a known name followed by <c>;</c> decodes; anything else stays as written.</summary>
        private string EntityReference( )
        {
            _position += 1;
            var start = _position;
            if ( Peek( 0 ) is { } first && (char.IsAsciiLetter( first ) || first is '_' or ':') )
            {
                while ( Peek( 0 ) is { } character && (char.IsAsciiLetterOrDigit( character ) || character is '_' or ':' or '.' or '-') )
                {
                    _position += 1;
                }
            }

            var name = text[start.._position];
            if ( name.Length > 0 && Peek( 0 ) == ';' && Html4Entities.CodePoints.TryGetValue( name, out var codePoint ) )
            {
                _position += 1;
                return char.ConvertFromUtf32( codePoint );
            }

            return $"&{name}";
        }
    }
}
