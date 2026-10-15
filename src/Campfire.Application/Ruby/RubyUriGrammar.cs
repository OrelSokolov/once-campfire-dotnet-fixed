using System.Globalization;
using System.Numerics;

namespace Campfire.Application.Ruby;

/// <summary>
/// RFC 3986 matching as <c>URI::RFC3986_Parser::RFC3986_URI</c> and <c>RFC3986_relative_ref</c> do
/// it, for ASCII input. The regular expressions' possessive quantifiers become greedy scans.
/// </summary>
internal static class RubyUriGrammar
{
    /// <summary>An absolute URI's components, or <c>null</c> when <c>RFC3986_URI</c> doesn't match.</summary>
    public static RubyUri? SplitAbsolute( string value )
    {
        var schemeEnd = SchemeEnd( value );
        if ( schemeEnd < 0 )
        {
            return null;
        }

        var restStart = schemeEnd + 1;
        if ( ParseTail( value, restStart ) is not { } tail )
        {
            return null;
        }

        var uri = new RubyUri
        {
            Scheme = value[..schemeEnd],
            Query = tail.Query is var (queryStart, queryEnd) ? value[queryStart..queryEnd] : null,
            Fragment = tail.Fragment is var (fragmentStart, fragmentEnd) ? value[fragmentStart..fragmentEnd] : null,
        };

        return SplitHierarchy( value, restStart, tail.HierarchyEnd, uri ) ? uri : null;
    }

    /// <summary><c>RFC3986_relative_ref</c>: only validity matters, since a relative reference is never HTTP.</summary>
    public static RubyUri? SplitRelative( string value )
    {
        if ( ParseTail( value, 0 ) is not { } tail )
        {
            return null;
        }

        var end = tail.HierarchyEnd;
        bool valid;
        if ( value.AsSpan( 0, end ).StartsWith( "//" ) )
        {
            valid = ParseAuthority( value, 2, end ) is { } authority && TakeWhile( value, authority.End, end, IsSegmentCharacter ) == end;
        }
        else if ( end == 0 || value[0] == '/' )
        {
            valid = TakeWhile( value, 0, end, IsSegmentCharacter ) == end;
        }
        else
        {
            var first = TakeWhile( value, 0, end, IsSegmentNoColonCharacter );
            valid = first > 0 && (first == end || (value[first] == '/' && TakeWhile( value, first, end, IsSegmentCharacter ) == end));
        }

        if ( !valid )
        {
            return null;
        }

        return new RubyUri
        {
            Path = value[..end],
            Query = tail.Query is var (queryStart, queryEnd) ? value[queryStart..queryEnd] : null,
            Fragment = tail.Fragment is var (fragmentStart, fragmentEnd) ? value[fragmentStart..fragmentEnd] : null,
        };
    }

    /// <summary>The end of <c>[A-Za-z][A-Za-z0-9+\-.]*:</c>, or -1.</summary>
    private static int SchemeEnd( string value )
    {
        if ( value.Length == 0 || !char.IsAsciiLetter( value[0] ) )
        {
            return -1;
        }

        var position = 1;
        while ( position < value.Length && (char.IsAsciiLetterOrDigit( value[position] ) || value[position] is '+' or '-' or '.') )
        {
            position++;
        }

        return position < value.Length && value[position] == ':' ? position : -1;
    }

    /// <summary>Fills in the hierarchy part: an authority with a path, an absolute path, or an opaque part.</summary>
    private static bool SplitHierarchy( string value, int start, int end, RubyUri uri )
    {
        var hierarchy = value.AsSpan( start, end - start );
        if ( hierarchy.StartsWith( "//" ) )
        {
            if ( ParseAuthority( value, start + 2, end ) is not { } authority )
            {
                return false;
            }

            uri.Userinfo = authority.Userinfo;
            uri.Host = authority.Host;
            uri.Port = authority.Port;
            uri.Path = value[authority.End..end];

            // path-abempty: (?:/seg*)?
            return authority.End == end || (value[authority.End] == '/' && TakeWhile( value, authority.End, end, IsSegmentCharacter ) == end);
        }

        if ( hierarchy.IsEmpty )
        {
            uri.Path = "";
            return true;
        }

        // path-absolute, or path-rootless (which becomes the opaque part, with the query folded back in)
        if ( TakeWhile( value, start, end, IsSegmentCharacter ) != end )
        {
            return false;
        }

        if ( hierarchy[0] == '/' )
        {
            uri.Path = value[start..end];
        }
        else
        {
            uri.Opaque = uri.Query is null ? value[start..end] : $"{value[start..end]}?{uri.Query}";
            uri.Query = null;
        }

        return true;
    }

    /// <summary>Splits off <c>(?:\?(?&lt;query&gt;[^#]*+))?(?:\#(?&lt;fragment&gt;FRAGMENT))?\z</c>.</summary>
    private static Tail? ParseTail( string value, int start )
    {
        var hierarchyEnd = value.AsSpan( start ).IndexOfAny( '?', '#' );
        hierarchyEnd = hierarchyEnd < 0 ? value.Length : start + hierarchyEnd;
        var position = hierarchyEnd;
        (int, int)? query = null;
        if ( position < value.Length && value[position] == '?' )
        {
            var queryEnd = value.IndexOf( '#', position + 1 );
            queryEnd = queryEnd < 0 ? value.Length : queryEnd;
            query = (position + 1, queryEnd);
            position = queryEnd;
        }

        (int, int)? fragment = null;
        if ( position < value.Length && value[position] == '#' )
        {
            if ( TakeWhile( value, position + 1, value.Length, IsFragmentCharacter ) != value.Length )
            {
                return null;
            }

            fragment = (position + 1, value.Length);
        }

        return new Tail( hierarchyEnd, query, fragment );
    }

    private static Authority? ParseAuthority( string value, int start, int limit )
    {
        var position = start;
        string? userinfo = null;
        var userinfoEnd = TakeWhile( value, position, limit, IsUserinfoCharacter );
        if ( userinfoEnd < limit && value[userinfoEnd] == '@' )
        {
            userinfo = value[position..userinfoEnd];
            position = userinfoEnd + 1;
        }

        var hostEnd = IpLiteralEnd( value, position, limit ) ?? TakeWhile( value, position, limit, IsUnreservedOrSubDelimiter );
        var host = value[position..hostEnd];
        var end = hostEnd;
        BigInteger? port = null;
        if ( end < limit && value[end] == ':' )
        {
            var digitsEnd = end + 1;
            while ( digitsEnd < limit && char.IsAsciiDigit( value[digitsEnd] ) )
            {
                digitsEnd++;
            }

            port = digitsEnd == end + 1 ? null : BigInteger.Parse( value.AsSpan( end + 1, digitsEnd - end - 1 ), CultureInfo.InvariantCulture );
            end = digitsEnd;
        }

        if ( end < limit && value[end] != '/' )
        {
            return null;
        }

        return new Authority( userinfo, host, port, end );
    }

    /// <summary>The IP-literal alternative of HOST (a bracketed IPv6 or IPvFuture address); returns its end.</summary>
    private static int? IpLiteralEnd( string value, int start, int limit )
    {
        if ( start >= limit || value[start] != '[' )
        {
            return null;
        }

        var closingBracketIndex = value.IndexOf( ']', start, limit - start );
        if ( closingBracketIndex < 0 )
        {
            return null;
        }

        var inner = value[(start + 1)..closingBracketIndex];
        return IsIpvFuture( inner ) || Ipv6Addresses.ParseStrict( inner ) is not null ? closingBracketIndex + 1 : null;
    }

    private static bool IsIpvFuture( string inner )
    {
        if ( inner.Length == 0 || inner[0] is not ('v' or 'V') )
        {
            return false;
        }

        var dotIndex = inner.IndexOf( '.', StringComparison.Ordinal );
        if ( dotIndex < 0 )
        {
            return false;
        }

        var hex = inner.AsSpan( 1, dotIndex - 1 );
        var rest = inner.AsSpan( dotIndex + 1 );

        foreach ( var character in rest )
        {
            if ( !IsUnreservedOrSubDelimiter( character ) && character != ':' )
            {
                return false;
            }
        }

        return !hex.IsEmpty && !hex.ContainsAnyExcept( AsciiCharacterSets.HexDigits ) && !rest.IsEmpty;
    }

    /// <summary>Consumes <c>(?:%\h\h|[class])*</c> and returns the end index.</summary>
    private static int TakeWhile( string value, int start, int limit, Func<char, bool> isAllowed )
    {
        var position = start;
        while ( position < limit )
        {
            if ( value[position] == '%' && position + 2 < limit && char.IsAsciiHexDigit( value[position + 1] ) && char.IsAsciiHexDigit( value[position + 2] ) )
            {
                position += 3;
            }
            else if ( isAllowed( value[position] ) )
            {
                position++;
            }
            else
            {
                return position;
            }
        }

        return position;
    }

    /// <summary><c>[!$&amp;-.0-9;=A-Z_a-z~]</c>: note that <c>&amp;-.</c> covers <c>&amp; ' ( ) * + , - .</c>.</summary>
    private static bool IsUnreservedOrSubDelimiter( char character ) =>
        character is '!' or '$' or (>= '&' and <= '.') or (>= '0' and <= '9') or ';' or '=' or (>= 'A' and <= 'Z') or '_' or (>= 'a' and <= 'z') or '~';

    private static bool IsSegmentCharacter( char character ) => IsUnreservedOrSubDelimiter( character ) || character is ':' or '@' or '/';

    private static bool IsSegmentNoColonCharacter( char character ) => IsUnreservedOrSubDelimiter( character ) || character == '@';

    private static bool IsFragmentCharacter( char character ) => IsUnreservedOrSubDelimiter( character ) || character is ':' or '@' or '/' or '?';

    private static bool IsUserinfoCharacter( char character ) => IsUnreservedOrSubDelimiter( character ) || character == ':';

    private sealed record Tail( int HierarchyEnd, (int Start, int End)? Query, (int Start, int End)? Fragment );

    private sealed record Authority( string? Userinfo, string Host, BigInteger? Port, int End );
}
