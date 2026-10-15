using System.Text;
using System.Text.Encodings.Web;

namespace Campfire.Web.Views.Rendering;

/// <summary>
/// <c>ERB::Util.html_escape</c>: only <c>&amp; &lt; &gt; &quot; '</c> are escaped, as <c>&amp;amp; &amp;lt; &amp;gt;
/// &amp;quot; &amp;#39;</c>; everything else, non-ASCII included, stays raw. Every slice renders with this encoder
/// (docs/development.md, presenters and fragments); partials and layouts inherit it from the slice that renders them.
/// </summary>
public sealed class RailsHtmlEncoder : HtmlEncoder
{
    public static readonly RailsHtmlEncoder Instance = new( );

    private RailsHtmlEncoder( )
    {
    }

    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override unsafe int FindFirstCharacterToEncode( char* text, int textLength )
    {
        var span = new ReadOnlySpan<char>( text, textLength );
        return span.IndexOfAny( Escaping.SpecialCharacters );
    }

    public override unsafe bool TryEncodeUnicodeScalar( int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten )
    {
        var replacement = Escaping.Entity( unicodeScalar );
        var destination = new Span<char>( buffer, bufferLength );
        if ( replacement is not null )
        {
            numberOfCharactersWritten = replacement.Length;
            return replacement.TryCopyTo( destination );
        }

        var rune = new Rune( unicodeScalar );
        numberOfCharactersWritten = rune.Utf16SequenceLength;
        return rune.TryEncodeToUtf16( destination, out _ );
    }

    public override bool WillEncode( int unicodeScalar ) => Escaping.Entity( unicodeScalar ) is not null;
}
