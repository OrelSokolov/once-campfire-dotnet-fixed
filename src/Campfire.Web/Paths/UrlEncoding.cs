using System.Globalization;
using System.Text;

namespace Campfire.Web.Paths;

/// <summary>
/// The escaping Rails' URL helpers apply: path segments through
/// <c>ActionDispatch::Journey::Router::Utils.escape_segment</c>, query strings through <c>Hash#to_query</c>
/// (<c>CGI.escape</c> on keys and values, pairs sorted).
/// </summary>
public static class UrlEncoding
{
    /// <summary>
    /// <c>escape_segment</c>: everything outside RFC 3986 unreserved characters, sub-delims, <c>:</c> and <c>@</c>
    /// is percent-encoded (upper-case hex, UTF-8 bytes).
    /// </summary>
    public static string EscapeSegment( string segment )
    {
        ArgumentNullException.ThrowIfNull( segment );

        return Escape( segment, IsSegmentChar, spaceAsPlus: false );
    }

    /// <summary><c>CGI.escape</c>: everything but <c>A-Za-z0-9_.-~</c> is percent-encoded and a space becomes <c>+</c>.</summary>
    public static string CgiEscape( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        return Escape( text, IsCgiUnreserved, spaceAsPlus: true );
    }

    /// <summary><c>ERB::Util.url_encode</c>: <c>CGI.escape</c> with spaces as <c>%20</c>.</summary>
    public static string UrlEncode( string text ) => CgiEscape( text ).Replace( "+", "%20", StringComparison.Ordinal );

    /// <summary>
    /// <c>Hash#to_query</c> as <c>url_for</c> uses it for the parameters a route doesn't consume: nil values are
    /// dropped, each pair is <c>CGI.escape(key)=CGI.escape(value)</c>, arrays expand to <c>key[]=</c> pairs in
    /// their order, and the resulting chunks are sorted.
    /// </summary>
    public static string ToQuery( IEnumerable<KeyValuePair<string, QueryValue>> parameters )
    {
        ArgumentNullException.ThrowIfNull( parameters );

        var chunks = new List<string>( );
        foreach ( var (key, value) in parameters )
        {
            if ( value.Many is { } many )
            {
                var arrayKey = CgiEscape( key + "[]" );
                chunks.Add( string.Join( '&', many.Select( item => arrayKey + "=" + CgiEscape( item ) ) ) );
            }
            else if ( value.One is { } one )
            {
                chunks.Add( CgiEscape( key ) + "=" + CgiEscape( one ) );
            }
        }

        chunks.Sort( StringComparer.Ordinal );
        return string.Join( '&', chunks );
    }

    /// <summary><paramref name="path"/> with <see cref="ToQuery"/> appended when any parameter is present.</summary>
    public static string WithQuery( string path, params ReadOnlySpan<KeyValuePair<string, QueryValue>> parameters )
    {
        var query = ToQuery( parameters.ToArray( ) );
        return query.Length == 0 ? path : path + "?" + query;
    }

    private static bool IsSegmentChar( byte value ) =>
        IsAsciiAlphanumeric( value ) || value is ( byte ) '-' or ( byte ) '.' or ( byte ) '_' or ( byte ) '~' or ( byte ) '!' or ( byte ) '$'
            or ( byte ) '&' or ( byte ) '\'' or ( byte ) '(' or ( byte ) ')' or ( byte ) '*' or ( byte ) '+' or ( byte ) ',' or ( byte ) ';'
            or ( byte ) '=' or ( byte ) ':' or ( byte ) '@';

    private static bool IsCgiUnreserved( byte value ) =>
        IsAsciiAlphanumeric( value ) || value is ( byte ) '_' or ( byte ) '.' or ( byte ) '-' or ( byte ) '~';

    private static bool IsAsciiAlphanumeric( byte value ) =>
        value is >= ( byte ) 'a' and <= ( byte ) 'z' or >= ( byte ) 'A' and <= ( byte ) 'Z' or >= ( byte ) '0' and <= ( byte ) '9';

    private static string Escape( string text, Func<byte, bool> keep, bool spaceAsPlus )
    {
        var bytes = Encoding.UTF8.GetBytes( text );
        if ( bytes.All( keep ) )
        {
            return text;
        }

        var builder = new StringBuilder( bytes.Length * 3 );
        foreach ( var value in bytes )
        {
            if ( keep( value ) )
            {
                builder.Append( ( char ) value );
            }
            else if ( spaceAsPlus && value == ( byte ) ' ' )
            {
                builder.Append( '+' );
            }
            else
            {
                builder.Append( '%' ).Append( value.ToString( "X2", CultureInfo.InvariantCulture ) );
            }
        }

        return builder.ToString( );
    }
}
