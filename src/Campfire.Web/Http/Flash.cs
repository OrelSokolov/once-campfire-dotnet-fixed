using System.Text.Json.Nodes;

namespace Campfire.Web.Http;

/// <summary>
/// <c>ActionDispatch::Flash::FlashHash</c> (rails <c>1a02651</c>), stored in the session under
/// <c>"flash"</c> as <c>{ "discard" =&gt; [], "flashes" =&gt; { ... } }</c>.
/// </summary>
public sealed class Flash
{
    public const string SessionKey = "flash";

    private readonly List<KeyValuePair<string, JsonNode?>> _flashes = [];
    private readonly HashSet<string> _discard = new( StringComparer.Ordinal );

    public Flash( )
    {
        Now = new FlashNow( this );
    }

    /// <summary><c>flash.now</c>: values shown on this request only.</summary>
    public FlashNow Now { get; }

    public bool IsEmpty => _flashes.Count == 0;

    public IEnumerable<string> Keys => _flashes.Select( entry => entry.Key );

    /// <summary>The flashes in insertion order.</summary>
    public IEnumerable<KeyValuePair<string, JsonNode?>> Entries => _flashes;

    /// <summary><c>flash[key]</c> / <c>flash[key] = value</c> (shown on the next request).</summary>
    public JsonNode? this[string key]
    {
        get => IndexOf( key ) is var index and >= 0 ? _flashes[index].Value : null;
        set
        {
            _discard.Remove( key );
            var index = IndexOf( key );
            if ( index < 0 )
            {
                _flashes.Add( new( key, value ) );
            }
            else
            {
                _flashes[index] = new( key, value );
            }
        }
    }

    public string? Notice
    {
        get => GetString( "notice" );
        set => this["notice"] = value;
    }

    public string? Alert
    {
        get => GetString( "alert" );
        set => this["alert"] = value;
    }

    public string? GetString( string key ) => this[key] is JsonValue value && value.TryGetValue<string>( out var text ) ? text : null;

    public bool ContainsKey( string key ) => IndexOf( key ) >= 0;

    /// <summary><c>flash.keep(key)</c>, or every key when <paramref name="key"/> is <c>null</c>.</summary>
    public void Keep( string? key = null )
    {
        if ( key is null )
        {
            _discard.Clear( );
        }
        else
        {
            _discard.Remove( key );
        }
    }

    /// <summary><c>flash.discard(key)</c>, or every key when <paramref name="key"/> is <c>null</c>.</summary>
    public void Discard( string? key = null )
    {
        if ( key is null )
        {
            _discard.UnionWith( Keys );
        }
        else
        {
            _discard.Add( key );
        }
    }

    /// <summary><c>flash.delete(key)</c>.</summary>
    public void Delete( string key )
    {
        _discard.Remove( key );
        var index = IndexOf( key );
        if ( index >= 0 )
        {
            _flashes.RemoveAt( index );
        }
    }

    /// <summary><c>flash.sweep</c>: drop the discarded flashes, then mark the rest for discard.</summary>
    public void Sweep( )
    {
        _flashes.RemoveAll( entry => _discard.Contains( entry.Key ) );
        _discard.Clear( );
        _discard.UnionWith( Keys );
    }

    /// <summary>
    /// <c>FlashHash.from_session_value</c>: everything loaded is marked for discard at the end of this
    /// request, minus what the previous request had already discarded.
    /// </summary>
    public static Flash FromSessionValue( JsonNode? value )
    {
        var flash = new Flash( );
        if ( value is not JsonObject stored || stored["flashes"] is not JsonObject flashes )
        {
            return flash;
        }

        var discarded = stored["discard"] is JsonArray list
            ? list.Select( item => item is JsonValue text && text.TryGetValue<string>( out var key ) ? key : null ).ToHashSet( StringComparer.Ordinal )
            : [];
        foreach ( var (key, flashValue) in flashes )
        {
            if ( !discarded.Contains( key ) )
            {
                flash._flashes.Add( new( key, flashValue?.DeepClone( ) ) );
                flash._discard.Add( key );
            }
        }

        return flash;
    }

    /// <summary><c>FlashHash#to_session_value</c>: <c>null</c> when nothing survives.</summary>
    public JsonNode? ToSessionValue( )
    {
        var keep = new JsonObject( );
        foreach ( var (key, value) in _flashes )
        {
            if ( !_discard.Contains( key ) )
            {
                keep[key] = value?.DeepClone( );
            }
        }

        return keep.Count == 0 ? null : new JsonObject { ["discard"] = new JsonArray( ), ["flashes"] = keep };
    }

    private int IndexOf( string key ) => _flashes.FindIndex( entry => entry.Key == key );
}
