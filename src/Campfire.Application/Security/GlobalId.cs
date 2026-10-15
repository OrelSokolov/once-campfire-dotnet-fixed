using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Campfire.Application.Security;

/// <summary>An opaque frontend record identifier; it grants no authority by itself.</summary>
public sealed record GlobalId( string App, string ModelName, string Id )
{
    public const string CampfireApp = "campfire";

    /// <summary>The URI scheme prefix of every Global ID, <c>gid://</c>.</summary>
    public const string Scheme = "gid://";

    public static GlobalId For( string modelName, long id ) => new( CampfireApp, modelName, id.ToString( CultureInfo.InvariantCulture ) );

    public static GlobalId For( string modelName, string id ) => new( CampfireApp, modelName, id );

    /// <summary>
    /// <c>GlobalID.parse</c>: <c>gid://app/Model/id</c>, the query string ignored; null for anything else, or a part
    /// left empty.
    /// </summary>
    public static GlobalId? Parse( string? value )
    {
        if ( value is null || !value.StartsWith( Scheme, StringComparison.Ordinal ) )
        {
            return null;
        }

        var parts = value[Scheme.Length..].Split( '?', 2 )[0].Split( '/', 3 );
        return parts.Length == 3 && parts.All( part => part.Length > 0 ) ? new GlobalId( parts[0], parts[1], parts[2] ) : null;
    }

    /// <summary><c>to_param</c>: the URI, URL-safe Base64 encoded.</summary>
    public string ToParam( ) => Base64Url.EncodeToString( Encoding.UTF8.GetBytes( ToString( ) ) );

    /// <summary><c>GlobalID.parse</c> of a <see cref="ToParam"/> value; null when it is not one.</summary>
    public static GlobalId? FromParam( string value )
    {
        try
        {
            return Parse( new UTF8Encoding( false, true ).GetString( Base64Url.DecodeFromChars( value ) ) );
        }
        catch ( Exception error ) when ( error is FormatException or DecoderFallbackException )
        {
            return null;
        }
    }

    public override string ToString( ) => $"{Scheme}{App}/{ModelName}/{Id}";
}
