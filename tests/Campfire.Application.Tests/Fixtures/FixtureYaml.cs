using System.Text;

namespace Campfire.Application.Tests.Fixtures;

/// <summary>
/// The YAML the Rails fixtures use, after ERB: a mapping of labels to mappings of scalars, indented by spaces, with
/// plain, single-quoted or double-quoted values and <c>#</c> comments. Anything else fails loudly rather than being
/// misread.
/// </summary>
internal static class FixtureYaml
{
    public static List<(string Label, List<(string Key, YamlScalar Value)> Row)> Parse( string yaml, string source )
    {
        var rows = new List<(string, List<(string, YamlScalar)>)>( );
        List<(string, YamlScalar)>? current = null;
        var lineNumber = 0;
        foreach ( var raw in yaml.Split( '\n' ) )
        {
            lineNumber++;
            var line = raw.TrimEnd( '\r' );
            if ( line.Trim( ).Length == 0 || line.TrimStart( ).StartsWith( '#' ) )
            {
                continue;
            }

            var (key, value) = SplitKey( line.Trim( ), source, lineNumber );
            if ( !char.IsWhiteSpace( line[0] ) )
            {
                if ( value.Length > 0 )
                {
                    throw Unsupported( source, lineNumber, "a top-level key with an inline value" );
                }

                current = [];
                rows.Add( (key, current) );
            }
            else if ( current is null )
            {
                throw Unsupported( source, lineNumber, "an indented key outside a fixture" );
            }
            else
            {
                current.Add( (key, Scalar( value, source, lineNumber )) );
            }
        }

        return rows;
    }

    private static (string Key, string Value) SplitKey( string line, string source, int lineNumber )
    {
        var colon = line.IndexOf( ':', StringComparison.Ordinal );
        while ( colon >= 0 && colon + 1 < line.Length && line[colon + 1] != ' ' )
        {
            colon = line.IndexOf( ':', colon + 1 );
        }

        if ( colon <= 0 )
        {
            throw Unsupported( source, lineNumber, $"a line that is not \"key: value\" ({line})" );
        }

        return (line[..colon].Trim( ), line[(colon + 1)..].Trim( ));
    }

    private static YamlScalar Scalar( string text, string source, int lineNumber )
    {
        if ( text.StartsWith( '"' ) || text.StartsWith( '\'' ) )
        {
            text = WithoutTrailingComment( text, source, lineNumber );
        }

        if ( text.StartsWith( '"' ) )
        {
            return new YamlScalar( DoubleQuoted( text, source, lineNumber ), Quoted: true );
        }

        if ( text.StartsWith( '\'' ) )
        {
            if ( text.Length < 2 || !text.EndsWith( '\'' ) )
            {
                throw Unsupported( source, lineNumber, "an unterminated single-quoted scalar" );
            }

            return new YamlScalar( text[1..^1].Replace( "''", "'", StringComparison.Ordinal ), Quoted: true );
        }

        if ( text.Length > 0 && "[{|>&*".Contains( text[0], StringComparison.Ordinal ) )
        {
            throw Unsupported( source, lineNumber, $"a collection, block, or alias scalar ({text})" );
        }

        var comment = text.IndexOf( " #", StringComparison.Ordinal );
        return new YamlScalar( (comment >= 0 ? text[..comment] : text).TrimEnd( ), Quoted: false );
    }

    private static string DoubleQuoted( string text, string source, int lineNumber )
    {
        if ( text.Length < 2 || !text.EndsWith( '"' ) )
        {
            throw Unsupported( source, lineNumber, "an unterminated double-quoted scalar" );
        }

        var value = new StringBuilder( );
        var body = text[1..^1];
        for ( var index = 0; index < body.Length; index++ )
        {
            if ( body[index] != '\\' )
            {
                value.Append( body[index] );
                continue;
            }

            index++;
            value.Append( body[index] switch
            {
                'n' => "\n",
                't' => "\t",
                'u' => (( char ) Convert.ToInt32( body.Substring( index + 1, 4 ), 16 )).ToString( ),
                _ => body[index].ToString( ),
            } );
            index += body[index] == 'u' ? 4 : 0;
        }

        return value.ToString( );
    }

    /// <summary>A quoted scalar up to its closing quote; only a comment may follow it.</summary>
    private static string WithoutTrailingComment( string text, string source, int lineNumber )
    {
        var close = text.LastIndexOf( text[0] );
        var rest = close > 0 ? text[(close + 1)..].Trim( ) : "";
        if ( rest.Length > 0 && !rest.StartsWith( '#' ) )
        {
            throw Unsupported( source, lineNumber, $"text after a quoted scalar ({text})" );
        }

        return close > 0 ? text[..(close + 1)] : text;
    }

    private static InvalidDataException Unsupported( string source, int lineNumber, string what ) =>
        new( $"{source}:{lineNumber}: the fixture loader does not support {what}" );
}
