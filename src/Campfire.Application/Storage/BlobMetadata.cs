using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;

namespace Campfire.Application.Storage;

/// <summary>
/// The <c>active_storage_blobs.metadata</c> column (<c>store :metadata, coder: ActiveRecord::Coders::JSON</c>):
/// an ordered JSON object. Key order is part of the stored bytes, so values keep insertion order and
/// <see cref="Merge"/> works like <c>Hash#merge</c>. Instances are immutable.
/// </summary>
public sealed class BlobMetadata
{
    /// <summary>Set once Active Storage has analyzed the file (dimensions, duration and the like).</summary>
    public const string AnalyzedKey = "analyzed";

    /// <summary>Set once the content type has been identified from the file's bytes.</summary>
    public const string IdentifiedKey = "identified";

    private readonly JsonObject _values;

    private BlobMetadata( JsonObject values )
    {
        _values = values;
    }

    public static BlobMetadata Empty { get; } = new( [] );

    /// <summary><c>{"identified": true}</c>, what <c>Blob#unfurl</c> leaves on a new blob.</summary>
    public static BlobMetadata Identified { get; } = new( new JsonObject { [IdentifiedKey] = true } );

    public int Count => _values.Count;

    /// <summary><c>analyzed?</c>: the <c>analyzed</c> key holds anything but <c>null</c> or <c>false</c>.</summary>
    public bool IsAnalyzed => IsTruthy( AnalyzedKey );

    /// <summary><c>identified?</c>.</summary>
    public bool IsIdentified => IsTruthy( IdentifiedKey );

    /// <summary>
    /// The column as Active Record loads it: a blank column is <c>{}</c>, and so is JSON that isn't an
    /// object (<c>IndifferentCoder.as_indifferent_hash</c>). Malformed JSON throws <see cref="JsonException"/>,
    /// as <c>ActiveSupport::JSON.decode</c> raises.
    /// </summary>
    public static BlobMetadata Parse( string? column )
    {
        if ( string.IsNullOrWhiteSpace( column ) )
        {
            return Empty;
        }

        return JsonPayload.Parse( column ) is JsonObject values ? new BlobMetadata( values ) : Empty;
    }

    /// <summary>Metadata holding a copy of <paramref name="values"/>.</summary>
    public static BlobMetadata From( JsonObject values )
    {
        ArgumentNullException.ThrowIfNull( values );

        return new BlobMetadata( ( JsonObject ) values.DeepClone( ) );
    }

    /// <summary>A copy of the value under <paramref name="key"/>; <c>null</c> when absent or JSON <c>null</c>.</summary>
    public JsonNode? Get( string key ) => _values.TryGetPropertyValue( key, out var value ) ? value?.DeepClone( ) : null;

    public bool ContainsKey( string key ) => _values.ContainsKey( key );

    /// <summary><c>metadata[:width]</c> and friends as the views read them: a JSON number, else <c>null</c>.</summary>
    public double? Number( string key )
    {
        if ( !_values.TryGetPropertyValue( key, out var value ) || value is not JsonValue scalar || scalar.GetValueKind( ) != JsonValueKind.Number )
        {
            return null;
        }

        return double.Parse( scalar.ToJsonString( ), CultureInfo.InvariantCulture );
    }

    /// <summary><c>Hash#merge</c>: keys of <paramref name="other"/> overwrite in place, new keys are appended in order.</summary>
    public BlobMetadata Merge( BlobMetadata other )
    {
        ArgumentNullException.ThrowIfNull( other );

        var merged = ( JsonObject ) _values.DeepClone( );
        foreach ( var (key, value) in other._values )
        {
            merged[key] = value?.DeepClone( );
        }

        return new BlobMetadata( merged );
    }

    /// <summary>This metadata with <paramref name="key"/> set, in place when present.</summary>
    public BlobMetadata With( string key, JsonNode? value ) => Merge( new BlobMetadata( new JsonObject { [key] = value?.DeepClone( ) } ) );

    /// <summary>The column bytes: <c>ActiveSupport::JSON.encode</c> (the coder's encoder escapes HTML entities).</summary>
    public string ToJson( ) => JsonPayload.Encode( _values );

    /// <summary>A mutable copy.</summary>
    public JsonObject ToJsonObject( ) => ( JsonObject ) _values.DeepClone( );

    public override string ToString( ) => ToJson( );

    private bool IsTruthy( string key ) =>
        _values.TryGetPropertyValue( key, out var value ) && value is not null && !(value is JsonValue scalar && scalar.GetValueKind( ) == JsonValueKind.False);
}
