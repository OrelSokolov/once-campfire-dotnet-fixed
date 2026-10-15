namespace Campfire.Application.RichText.AutoLinking;

/// <summary>
/// What rails_autolink's <c>auto_linked?(left, right)</c> asks about the text around a match, where
/// <c>left</c> is everything before it and <c>right</c> everything after. rails_autolink runs its
/// regular expressions over the whole of <c>left</c> for every match, which is quadratic (cubic for
/// <c>rindex</c>); this indexes the text once so that each question is a binary search, with the same
/// answers (hardening from Rust <c>20c9f43</c>: autolinking in linear time).
/// </summary>
internal sealed class AutoLinkTagIndex
{
    /// <summary>Positions of <c>&lt;</c> and of <c>&gt;</c>, ascending.</summary>
    private readonly List<int> _lessThans = [];

    private readonly List<int> _greaterThans = [];

    /// <summary>(start, end) of each <c>AUTO_LINK_CRE[2]</c> (<c>/&lt;a\b.*?&gt;/i</c>) match: ascending and disjoint.</summary>
    private readonly List<(int Start, int End)> _openAnchors = [];

    /// <summary>Positions of <c>&lt;/a&gt;</c> (<c>AUTO_LINK_CRE[3]</c>, in any case), ascending.</summary>
    private readonly List<int> _closeAnchors = [];

    /// <summary>
    /// The first <c>\n</c> that ends a line inside an unclosed tag, after which <c>AUTO_LINK_CRE[0]</c>
    /// (<c>/&lt;[^&gt;]+$/</c>, whose <c>$</c> also matches before a newline) matches every <c>left</c>.
    /// </summary>
    private readonly int? _firstDanglingNewline;

    public AutoLinkTagIndex( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        // The first `<` since the last `>`
        int? unclosedLessThan = null;
        for ( var position = 0; position < text.Length; position++ )
        {
            switch ( text[position] )
            {
                case '<':
                    _lessThans.Add( position );
                    unclosedLessThan ??= position;
                    IndexAnchors( text, position );
                    break;
                case '>':
                    _greaterThans.Add( position );
                    unclosedLessThan = null;
                    break;
                case '\n' when _firstDanglingNewline is null && unclosedLessThan is { } lessThan && lessThan + 2 <= position:
                    _firstDanglingNewline = position;
                    break;
            }
        }
    }

    /// <summary><c>auto_linked?(text[..start], text[end..])</c>: inside a tag, or inside an unclosed <c>&lt;a&gt;</c>.</summary>
    public bool IsAutoLinked( int start, int end ) => (OpenTagAtLineEnd( start ) && ClosesTag( end )) || InsideAnchor( start );

    private void IndexAnchors( string text, int start )
    {
        if ( start + 4 <= text.Length && string.Compare( text, start + 1, "/a>", 0, 3, StringComparison.OrdinalIgnoreCase ) == 0 )
        {
            _closeAnchors.Add( start );
        }

        var afterPrevious = _openAnchors.Count == 0 || _openAnchors[^1].End <= start;
        if ( afterPrevious && OpenAnchorEnd( text, start ) is { } end )
        {
            _openAnchors.Add( (start, end) );
        }
    }

    /// <summary>
    /// The end of <c>/&lt;a\b.*?&gt;/i</c> matched at <paramref name="start"/>: Ruby's <c>\b</c> is an ASCII
    /// word boundary, and <c>.</c> stops at a newline.
    /// </summary>
    private static int? OpenAnchorEnd( string text, int start )
    {
        if ( start + 1 >= text.Length || text[start + 1] is not ('a' or 'A') )
        {
            return null;
        }

        if ( start + 2 < text.Length && (char.IsAsciiLetterOrDigit( text[start + 2] ) || text[start + 2] == '_') )
        {
            return null;
        }

        for ( var position = start + 2; position < text.Length && text[position] != '\n'; position++ )
        {
            if ( text[position] == '>' )
            {
                return position + 1;
            }
        }

        return null;
    }

    /// <summary><c>left =~ /&lt;[^&gt;]+$/</c></summary>
    private bool OpenTagAtLineEnd( int start )
    {
        if ( _firstDanglingNewline is { } newline && newline < start )
        {
            return true;
        }

        // At the end of `left`: a `<` after the last `>`, with at least one character after it
        var lastGreaterThanIndex = LowerBound( _greaterThans, start ) - 1;
        var firstLessThanIndex = lastGreaterThanIndex < 0 ? 0 : UpperBound( _lessThans, _greaterThans[lastGreaterThanIndex] );

        return firstLessThanIndex < _lessThans.Count && _lessThans[firstLessThanIndex] + 2 <= start;
    }

    /// <summary><c>right =~ /^[^&gt;]*&gt;/</c>, which matches whenever <c>right</c> has a <c>&gt;</c> at all.</summary>
    private bool ClosesTag( int end ) => _greaterThans.Count > 0 && _greaterThans[^1] >= end;

    /// <summary>
    /// <c>(i = left.rindex(/&lt;a\b.*?&gt;/i)) &amp;&amp; left[i..] !~ /&lt;\/a&gt;/i</c>: the last
    /// <c>&lt;a ...&gt;</c> wholly in <c>left</c> isn't closed before <c>left</c> ends.
    /// </summary>
    private bool InsideAnchor( int start )
    {
        var before = PartitionPoint( _openAnchors, anchor => anchor.End <= start );
        if ( before == 0 )
        {
            return false;
        }

        var anchorEnd = _openAnchors[before - 1].End;
        var closeIndex = LowerBound( _closeAnchors, anchorEnd );

        return !(closeIndex < _closeAnchors.Count && _closeAnchors[closeIndex] + 4 <= start);
    }

    /// <summary>The number of positions below <paramref name="value"/>.</summary>
    private static int LowerBound( List<int> sorted, int value )
    {
        var index = sorted.BinarySearch( value );

        return index >= 0 ? index : ~index;
    }

    /// <summary>The number of positions at or below <paramref name="value"/>.</summary>
    private static int UpperBound( List<int> sorted, int value )
    {
        var index = sorted.BinarySearch( value );

        return index >= 0 ? index + 1 : ~index;
    }

    /// <summary>The length of the prefix of <paramref name="sorted"/> whose items satisfy a monotone predicate.</summary>
    private static int PartitionPoint( List<(int Start, int End)> sorted, Func<(int Start, int End), bool> isBefore )
    {
        int low = 0, high = sorted.Count;
        while ( low < high )
        {
            var middle = low + ((high - low) / 2);
            if ( isBefore( sorted[middle] ) )
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
