namespace Campfire.Web.Policy;

/// <summary>
/// <c>UserAgent::Version</c> (useragent 0.16.11). Equality is string equality; ordering is the gem's
/// <c>&lt;=&gt;</c>, which is not a total order (a non-numeric version sorts below everything it isn't
/// equal to, from either side). Ported from Rust <c>crates/campfire/src/concerns/user_agent.rs</c>.
/// </summary>
public sealed class UserAgentVersion : IEquatable<UserAgentVersion>
{
    private readonly bool _comparable;

    public UserAgentVersion( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        Text = text;
        IsNil = text.All( UserAgent.IsRubySpace );
        var digits = text.TakeWhile( char.IsAsciiDigit ).Count( );
        _comparable = !IsNil && digits > 0 && (digits == text.Length || text[digits] == '.');
        if ( IsNil )
        {
            Segments = [];
        }
        else if ( _comparable )
        {
            Segments = ScanSegments( text );
        }
        else
        {
            Segments = [new VersionSegment( false, text )];
        }
    }

    /// <summary><c>Version.new(nil)</c>.</summary>
    public static UserAgentVersion Empty { get; } = new( "" );

    public string Text { get; }

    /// <summary><c>Version#nil?</c>: the string is empty or whitespace.</summary>
    public bool IsNil { get; }

    /// <summary><c>version.to_s.present?</c> (ActiveSupport's Unicode-whitespace <c>blank?</c>).</summary>
    public bool IsPresent => UserAgent.IsPresent( Text );

    /// <summary><c>Version#to_a</c>.</summary>
    public IReadOnlyList<VersionSegment> Segments { get; }

    public static bool operator <( UserAgentVersion left, UserAgentVersion right ) => Compare( left, right ) < 0;

    public static bool operator >( UserAgentVersion left, UserAgentVersion right ) => Compare( left, right ) > 0;

    public static bool operator <=( UserAgentVersion left, UserAgentVersion right ) => Compare( left, right ) <= 0;

    public static bool operator >=( UserAgentVersion left, UserAgentVersion right ) => Compare( left, right ) >= 0;

    public static bool operator ==( UserAgentVersion? left, UserAgentVersion? right ) => left?.Text == right?.Text;

    public static bool operator !=( UserAgentVersion? left, UserAgentVersion? right ) => !(left == right);

    /// <summary><c>Version#&lt;=&gt;</c> against another version: only the first six segments count.</summary>
    public static int Compare( UserAgentVersion left, UserAgentVersion right )
    {
        ArgumentNullException.ThrowIfNull( left );
        ArgumentNullException.ThrowIfNull( right );

        if ( !left._comparable )
        {
            return left.Text == right.Text ? 0 : -1;
        }

        var zero = VersionSegment.Number( "0" );
        for ( var index = 0; index < 6; index++ )
        {
            var leftSegment = index < left.Segments.Count ? left.Segments[index] : zero;
            var rightSegment = index < right.Segments.Count ? right.Segments[index] : zero;
            if ( leftSegment == rightSegment )
            {
                continue;
            }

            return (leftSegment.IsNumber, rightSegment.IsNumber) switch
            {
                (false, true ) => -1,
                (true, false ) => 1,
                (true, true ) => CompareNumbers( leftSegment.Value, rightSegment.Value ),
                _ => Math.Sign( string.CompareOrdinal( leftSegment.Value, rightSegment.Value ) ),
            };
        }

        return 0;
    }

    /// <summary>Two number segments (leading zeros stripped): the longer is larger; equal lengths compare by digits.</summary>
    private static int CompareNumbers( string left, string right )
    {
        if ( left.Length != right.Length )
        {
            return left.Length.CompareTo( right.Length );
        }

        return Math.Sign( string.CompareOrdinal( left, right ) );
    }

    public bool Equals( UserAgentVersion? other ) => other is not null && other.Text == Text;

    public override bool Equals( object? obj ) => obj is UserAgentVersion other && Equals( other );

    public override int GetHashCode( ) => Text.GetHashCode( StringComparison.Ordinal );

    public override string ToString( ) => Text;

    /// <summary><c>str.scan(/\d+|[A-Za-z][0-9A-Za-z-]*$/)</c>.</summary>
    private static List<VersionSegment> ScanSegments( string text )
    {
        var segments = new List<VersionSegment>( );
        var position = 0;
        while ( position < text.Length )
        {
            if ( char.IsAsciiDigit( text[position] ) )
            {
                var start = position;
                while ( position < text.Length && char.IsAsciiDigit( text[position] ) )
                {
                    position++;
                }

                segments.Add( VersionSegment.Number( text[start..position] ) );
                continue;
            }

            if ( char.IsAsciiLetter( text[position] ) && WordRunsToEnd( text, position ) )
            {
                segments.Add( new VersionSegment( false, text[position..] ) );
                break;
            }

            position++;
        }

        return segments;
    }

    private static bool WordRunsToEnd( string text, int start )
    {
        var end = start + 1;
        while ( end < text.Length && (char.IsAsciiLetterOrDigit( text[end] ) || text[end] == '-') )
        {
            end++;
        }

        return end == text.Length;
    }
}
