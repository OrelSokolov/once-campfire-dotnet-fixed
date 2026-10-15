using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Storage.Marcel;

namespace Campfire.Application.Storage;

/// <summary>Owned transformations with a canonical JSON digest. Only Campfire's media operations are allowed.</summary>
public sealed class Variation : IEquatable<Variation>
{
    private readonly JsonObject _transformations;
    public Variation( JsonObject transformations )
    {
        ArgumentNullException.ThrowIfNull( transformations );
        _transformations = ( JsonObject ) transformations.DeepClone( );
        foreach ( var (name, _) in _transformations )
        {
            if ( name is not ("format" or "resize_to_limit") )
            {
                throw new StorageException( StorageFailure.InvalidVariation, "Unsupported media transformation: " + name );
            }
        }
    }
    public JsonObject Transformations => ( JsonObject ) _transformations.DeepClone( );
    public bool IsEmpty => _transformations.Count == 0;
    public JsonNode? this[string name] => _transformations[name]?.DeepClone( );
    public static Variation ResizeToLimit( long width, long height, string? format = null )
    {
        var values = new JsonObject { ["resize_to_limit"] = new JsonArray( JsonValue.Create( width ), JsonValue.Create( height ) ) };
        if ( format is not null )
        {
            values["format"] = format;
        }

        return new Variation( values );
    }
    public static Variation FormatOnly( string format ) => new( new JsonObject { ["format"] = format } );
    public Variation DefaultTo( string key, string value )
    {
        var values = Transformations;
        if ( !values.ContainsKey( key ) )
        {
            values[key] = value;
        }

        return new Variation( values );
    }
    public string Digest( ) => Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( CanonicalJson( ) ) ) );
    public string Format( )
    {
        var format = _transformations["format"]?.GetValue<string>( ) ?? "png";
        if ( MarcelMimeType.ByExtension( format ) is null )
        {
            throw new StorageException( StorageFailure.InvalidVariation, "Invalid variant format: " + format );
        }

        return format;
    }
    public string ContentType( ) => MarcelMimeType.ForExtension( Format( ) );
    public string Key( ActiveStorageVerifier verifier ) => verifier.Generate( CanonicalJson( ), ActiveStorageVerifier.VariationPurpose );
    public static Variation Decode( ActiveStorageVerifier verifier, string key, DateTimeOffset now )
    {
        var data = verifier.VerifiedNode( key, ActiveStorageVerifier.VariationPurpose, now, out var valid );
        if ( !valid )
        {
            throw new StorageException( StorageFailure.InvalidSignature, "Invalid variation token." );
        }

        return FromJson( data );
    }
    public static Variation FromJson( JsonNode? data )
    {
        if ( data is not JsonObject values )
        {
            throw new StorageException( StorageFailure.InvalidVariation, "Expected transformation object." );
        }

        return new Variation( values );
    }
    private string CanonicalJson( ) => new JsonObject( _transformations.OrderBy( pair => pair.Key, StringComparer.Ordinal )
        .Select( pair => new KeyValuePair<string, JsonNode?>( pair.Key, pair.Value?.DeepClone( ) ) ) ).ToJsonString( );
    public bool Equals( Variation? other ) => other is not null && CanonicalJson( ) == other.CanonicalJson( );
    public override bool Equals( object? obj ) => Equals( obj as Variation );
    public override int GetHashCode( ) => StringComparer.Ordinal.GetHashCode( CanonicalJson( ) );
    public override string ToString( ) => CanonicalJson( );
}
