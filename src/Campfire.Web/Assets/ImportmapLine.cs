namespace Campfire.Web.Assets;

/// <summary>
/// One statement of <c>config/importmap.rb</c>: <c>pin "name", to: "...", preload: false</c> or
/// <c>pin_all_from "dir", under: "...", to: "..."</c>, with string literals in single or double
/// quotes and an optional trailing comment. Other Ruby is rejected with an exception.
/// </summary>
internal abstract record ImportmapLine
{
    public sealed record PinLine( string Name, string? To, bool Preload ) : ImportmapLine;

    public sealed record DirectoryLine( string Directory, string? Under, string? To, bool Preload ) : ImportmapLine;

    /// <summary>The statement on <paramref name="line"/>, or <c>null</c> for a blank or comment line.</summary>
    public static ImportmapLine? Parse( string line )
    {
        var text = line.Trim( );
        if ( text.Length == 0 || text.StartsWith( '#' ) )
        {
            return null;
        }

        var space = text.IndexOfAny( [' ', '\t'] );
        var command = space >= 0 ? text[..space] : text;
        var reader = new Reader( space >= 0 ? text[space..].TrimStart( ) : string.Empty, line );

        var first = reader.String( );
        if ( first is null )
        {
            throw new InvalidDataException( $"importmap.rb: expected a string in {line}" );
        }

        var options = reader.Options( );

        return command switch
        {
            "pin" => new PinLine( first, options.To, options.Preload ),
            "pin_all_from" => new DirectoryLine( first, options.Under, options.To, options.Preload ),
            _ => throw new InvalidDataException( $"importmap.rb: unsupported statement {command}" ),
        };
    }

    private sealed record PinOptions( string? To, string? Under, bool Preload );

    private sealed class Reader( string rest, string line )
    {
        private string _rest = rest;

        public string? String( )
        {
            if ( _rest.Length == 0 || _rest[0] is not ('"' or '\'') )
            {
                return null;
            }

            var end = _rest.IndexOf( _rest[0], 1 );
            if ( end < 0 )
            {
                return null;
            }

            var value = _rest[1..end];
            _rest = _rest[(end + 1)..].TrimStart( );

            return value;
        }

        public PinOptions Options( )
        {
            string? to = null;
            string? under = null;
            var preload = true;

            while ( _rest.Length > 0 && !_rest.StartsWith( '#' ) )
            {
                if ( !_rest.StartsWith( ',' ) )
                {
                    throw new InvalidDataException( $"importmap.rb: can't parse {line}" );
                }

                var colon = _rest.IndexOf( ':', StringComparison.Ordinal );
                if ( colon < 0 )
                {
                    throw new InvalidDataException( $"importmap.rb: expected key: value in {line}" );
                }

                var key = _rest[1..colon].Trim( );
                _rest = _rest[(colon + 1)..].TrimStart( );

                switch ( key )
                {
                    case "to":
                        to = RequiredString( key );
                        break;
                    case "under":
                        under = RequiredString( key );
                        break;
                    case "preload":
                        preload = Boolean( );
                        break;
                    default:
                        throw new InvalidDataException( $"importmap.rb: unsupported option {key} in {line}" );
                }
            }

            return new PinOptions( to, under, preload );
        }

        private string RequiredString( string key )
        {
            var value = String( );
            if ( value is null )
            {
                throw new InvalidDataException( $"importmap.rb: {key} needs a string in {line}" );
            }

            return value;
        }

        private bool Boolean( )
        {
            foreach ( var literal in new[] { "true", "false" } )
            {
                if ( _rest.StartsWith( literal, StringComparison.Ordinal ) )
                {
                    _rest = _rest[literal.Length..].TrimStart( );
                    return literal == "true";
                }
            }

            throw new InvalidDataException( $"importmap.rb: preload needs true or false in {line}" );
        }
    }
}
