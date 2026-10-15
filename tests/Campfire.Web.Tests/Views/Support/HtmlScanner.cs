using System.Globalization;
using System.Text;

namespace Campfire.Web.Tests.Views.Support;

/// <summary>Start-tag parsing, character-reference decoding and whitespace collapsing shared by the normalizers.</summary>
internal static class HtmlScanner
{
    private static readonly Dictionary<string, string> NamedReferences = new( StringComparer.Ordinal )
    {
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
        ["quot"] = "\"",
        ["apos"] = "'",
        ["nbsp"] = " ",
        ["trade"] = "™",
        ["hellip"] = "…",
        ["rarr"] = "→",
        ["larr"] = "←",
        ["mdash"] = "—",
        ["ndash"] = "–",
        ["copy"] = "©",
    };

    /// <summary>Parses the start tag at <paramref name="start"/>: its lower-cased name, attributes (first of duplicates
    /// kept, values decoded) and the index after it.</summary>
    public static (string Name, List<KeyValuePair<string, string>> Attributes, int Next) StartTag( string html, int start )
    {
        var index = start + 1;
        var nameEnd = index;
        while ( nameEnd < html.Length && !char.IsWhiteSpace( html[nameEnd] ) && html[nameEnd] is not '>' and not '/' )
        {
            nameEnd++;
        }

        var name = html[index..nameEnd].ToLowerInvariant( );
        index = nameEnd;
        var attributes = new List<KeyValuePair<string, string>>( );
        while ( true )
        {
            while ( index < html.Length && (char.IsWhiteSpace( html[index] ) || html[index] == '/') )
            {
                index++;
            }

            if ( index >= html.Length || html[index] == '>' )
            {
                return (name, attributes, Math.Min( index + 1, html.Length ));
            }

            var (key, value, next) = Attribute( html, index );
            index = next;
            if ( key.Length > 0 && !attributes.Any( pair => pair.Key == key ) )
            {
                attributes.Add( new( key, value ) );
            }
        }
    }

    /// <summary>Decodes numeric and common named character references; unknown ones stay as written.</summary>
    public static string Decode( string text )
    {
        if ( !text.Contains( '&', StringComparison.Ordinal ) )
        {
            return text;
        }

        var builder = new StringBuilder( text.Length );
        var index = 0;
        while ( index < text.Length )
        {
            var ampersand = text.IndexOf( '&', index );
            if ( ampersand < 0 )
            {
                builder.Append( text, index, text.Length - index );
                break;
            }

            builder.Append( text, index, ampersand - index );
            var semicolon = text.IndexOf( ';', ampersand );
            if ( semicolon > ampersand && semicolon - ampersand <= 12 && TryReference( text[(ampersand + 1)..semicolon], out var decoded ) )
            {
                builder.Append( decoded );
                index = semicolon + 1;
            }
            else
            {
                builder.Append( '&' );
                index = ampersand + 1;
            }
        }

        return builder.ToString( );
    }

    /// <summary>ASCII whitespace runs become one space.</summary>
    public static string CollapseWhitespace( string text )
    {
        var builder = new StringBuilder( text.Length );
        var inSpace = false;
        foreach ( var character in text )
        {
            if ( character is ' ' or '\t' or '\n' or '\r' or '\f' )
            {
                if ( !inSpace )
                {
                    builder.Append( ' ' );
                }

                inSpace = true;
            }
            else
            {
                builder.Append( character );
                inSpace = false;
            }
        }

        return builder.ToString( );
    }

    private static (string Key, string Value, int Next) Attribute( string html, int index )
    {
        var keyEnd = index;
        while ( keyEnd < html.Length && !char.IsWhiteSpace( html[keyEnd] ) && html[keyEnd] is not '=' and not '>' and not '/' )
        {
            keyEnd++;
        }

        var key = html[index..keyEnd].ToLowerInvariant( );
        index = keyEnd;
        while ( index < html.Length && char.IsWhiteSpace( html[index] ) )
        {
            index++;
        }

        if ( index >= html.Length || html[index] != '=' )
        {
            return (key, string.Empty, keyEnd == index && keyEnd < html.Length && html[keyEnd] == '/' ? index + 1 : index);
        }

        index++;
        while ( index < html.Length && char.IsWhiteSpace( html[index] ) )
        {
            index++;
        }

        var (value, next) = AttributeValue( html, index );
        return (key, value, next);
    }

    private static (string Value, int Next) AttributeValue( string html, int index )
    {
        if ( index < html.Length && html[index] is '"' or '\'' )
        {
            var quote = html[index];
            var end = html.IndexOf( quote, index + 1 );
            end = end < 0 ? html.Length : end;
            return (Decode( html[(index + 1)..end] ), Math.Min( end + 1, html.Length ));
        }

        var stop = index;
        while ( stop < html.Length && !char.IsWhiteSpace( html[stop] ) && html[stop] != '>' )
        {
            stop++;
        }

        return (Decode( html[index..stop] ), stop);
    }

    private static bool TryReference( string entity, out string decoded )
    {
        decoded = string.Empty;
        if ( entity.StartsWith( "#x", StringComparison.OrdinalIgnoreCase ) )
        {
            if ( int.TryParse( entity[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex ) && hex is > 0 and <= 0x10FFFF )
            {
                decoded = char.ConvertFromUtf32( hex );
                return true;
            }

            return false;
        }

        if ( entity.StartsWith( '#' ) )
        {
            if ( int.TryParse( entity[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var number ) && number is > 0 and <= 0x10FFFF )
            {
                decoded = char.ConvertFromUtf32( number );
                return true;
            }

            return false;
        }

        if ( NamedReferences.TryGetValue( entity, out var named ) )
        {
            decoded = named;
            return true;
        }

        return false;
    }
}
