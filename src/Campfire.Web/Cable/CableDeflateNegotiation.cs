using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Campfire.Web.Cable;

/// <summary>RFC 7692 offers: a shared 15-bit server window and no context takeover in either direction.</summary>
internal sealed record CableDeflateNegotiation( string Response, int ClientWindow )
{
    private const string Extension = "permessage-deflate";
    private const string NoServerContext = "server_no_context_takeover";
    private const string NoClientContext = "client_no_context_takeover";
    private const string ServerWindow = "server_max_window_bits";
    private const string ClientWindowName = "client_max_window_bits";

    internal static CableDeflateNegotiation? Parse( StringValues headers )
    {
        foreach ( var line in headers )
        {
            foreach ( var offer in Offers( line ?? string.Empty ) )
            {
                if ( ParseOffer( offer ) is { } accepted )
                {
                    return accepted;
                }
            }
        }

        return null;
    }

    private static List<string> Offers( string line )
    {
        var offers = new List<string>( );
        var quoted = false;
        var start = 0;
        for ( var index = 0; index < line.Length; index++ )
        {
            if ( line[index] == '\\' )
            {
                // No supported parameter needs an escaped decimal value.
                return [];
            }

            if ( line[index] == '"' )
            {
                quoted = !quoted;
            }
            else if ( line[index] == ',' && !quoted )
            {
                offers.Add( line[start..index].Trim( ) );
                start = index + 1;
            }
        }

        if ( quoted )
        {
            return [];
        }

        offers.Add( line[start..].Trim( ) );
        return offers;
    }

    private static CableDeflateNegotiation? ParseOffer( string offer )
    {
        var parts = offer.Split( ';', StringSplitOptions.TrimEntries );
        if ( parts[0] != Extension )
        {
            return null;
        }

        var seen = new HashSet<string>( StringComparer.Ordinal );
        var clientWindow = 15;
        foreach ( var part in parts.Skip( 1 ) )
        {
            var pair = part.Split( '=', 2, StringSplitOptions.TrimEntries );
            var name = pair[0];
            if ( !seen.Add( name ) || !AcceptParameter( name, pair.Length == 2 ? pair[1] : null, ref clientWindow ) )
            {
                return null;
            }
        }

        var response = $"{Extension}; {NoServerContext}; {NoClientContext}";
        if ( seen.Contains( ServerWindow ) )
        {
            response += $"; {ServerWindow}=15";
        }

        if ( seen.Contains( ClientWindowName ) )
        {
            response += $"; {ClientWindowName}={clientWindow.ToString( CultureInfo.InvariantCulture )}";
        }

        return new CableDeflateNegotiation( response, clientWindow );
    }

    private static bool AcceptParameter( string name, string? value, ref int clientWindow )
    {
        if ( name is NoServerContext or NoClientContext )
        {
            return value is null;
        }

        if ( name == ClientWindowName && value is null )
        {
            return true;
        }

        if ( name is not (ServerWindow or ClientWindowName) || value is null )
        {
            return false;
        }

        // RFC 6455 allows a quoted token as the value, but escaping cannot produce a decimal token.
        if ( value.StartsWith( '"' ) && value.EndsWith( '"' ) && value.Length >= 2 )
        {
            value = value[1..^1];
        }

        if ( value is not ("8" or "9" or "10" or "11" or "12" or "13" or "14" or "15") )
        {
            return false;
        }

        var window = int.Parse( value, CultureInfo.InvariantCulture );
        if ( name == ServerWindow )
        {
            return window == 15;
        }

        clientWindow = window;
        return true;
    }
}
