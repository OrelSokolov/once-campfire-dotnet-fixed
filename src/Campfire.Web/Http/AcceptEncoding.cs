namespace Campfire.Web.Http;

/// <summary><c>Rack::Request#accept_encoding</c> and <c>Rack::Utils.select_best_encoding</c> (rack 3.2.6).</summary>
public static class AcceptEncoding
{
    /// <summary><c>parse_http_accept_header</c>: <c>[(coding, quality)]</c>, quality from a leading <c>q=</c>.</summary>
    public static IReadOnlyList<(string Coding, double Quality)> Parse( string? header )
    {
        var result = new List<(string, double)>( );
        foreach ( var raw in (header ?? "").Split( ',' ) )
        {
            var part = raw.Trim( );
            if ( part.Length == 0 )
            {
                continue;
            }

            var semicolon = part.IndexOf( ';', StringComparison.Ordinal );
            var coding = (semicolon < 0 ? part : part[..semicolon]).Trim( );
            var parameters = semicolon < 0 ? null : part[(semicolon + 1)..].Trim( );
            var quality = 1.0;
            if ( parameters is not null && parameters.StartsWith( "q=", StringComparison.Ordinal ) )
            {
                var digits = new string( parameters[2..].TakeWhile( character => char.IsAsciiDigit( character ) || character == '.' ).ToArray( ) );
                if ( digits.Length > 0 )
                {
                    quality = AcceptHeader.RubyToF( digits );
                }
            }

            result.Add( (coding, quality) );
        }

        return result;
    }

    /// <summary>
    /// <c>select_best_encoding(available, accept)</c>: the first 16 codings, <c>*</c> expanded to the
    /// available ones not named, sorted by quality then by preference, <c>identity</c> implied, and
    /// anything with quality 0 removed. <c>null</c> when nothing is acceptable.
    /// </summary>
    public static string? SelectBest( IReadOnlyList<string> available, IReadOnlyList<(string Coding, double Quality)> accept )
    {
        ArgumentNullException.ThrowIfNull( available );
        ArgumentNullException.ThrowIfNull( accept );

        var considered = accept.Take( 16 ).ToList( );
        var expanded = new List<(string Coding, double Quality, int Preference)>( );
        var wildcardSeen = false;
        foreach ( var (coding, quality) in considered )
        {
            var preference = PreferenceOf( available, coding );
            if ( coding != "*" )
            {
                expanded.Add( (coding, quality, preference) );
            }
            else if ( !wildcardSeen )
            {
                wildcardSeen = true;
                foreach ( var other in available.Where( availableCoding => !considered.Any( entry => entry.Coding == availableCoding ) ) )
                {
                    expanded.Add( (other, quality, preference) );
                }
            }
        }

        var candidates = expanded.OrderByDescending( entry => entry.Quality ).ThenBy( entry => entry.Preference ).Select( entry => entry.Coding ).ToList( );
        if ( !candidates.Contains( ContentCodings.Identity ) )
        {
            candidates.Add( ContentCodings.Identity );
        }

        foreach ( var (coding, quality, _) in expanded )
        {
            if ( quality == 0.0 )
            {
                candidates.RemoveAll( candidate => candidate == coding );
            }
        }

        return candidates.FirstOrDefault( available.Contains );
    }

    /// <summary>The coding's position among <paramref name="available"/>; a coding not offered sorts after them all.</summary>
    private static int PreferenceOf( IReadOnlyList<string> available, string coding )
    {
        for ( var index = 0; index < available.Count; index++ )
        {
            if ( available[index] == coding )
            {
                return index;
            }
        }

        return available.Count;
    }
}
