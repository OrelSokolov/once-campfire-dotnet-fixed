namespace Campfire.Web.Http;

/// <summary>
/// <c>response.cache_control</c>: the directive hash <c>expires_in</c>, <c>fresh_when(public:)</c>,
/// <c>no_store</c> and friends fill, normalized like
/// <c>ActionDispatch::Http::Cache::Response#merge_and_normalize_cache_control!</c> (rails <c>1a02651</c>).
/// Like the Ruby hash, a key assigned <c>null</c> is present and overrides on merge.
/// </summary>
public sealed class CacheControl
{
    public const string DefaultCacheControl = "max-age=0, private, must-revalidate";

    /// <summary>The directive hash's keys, as Rails names them (<c>:no_store</c>, <c>:max_age</c>).</summary>
    public static class Keys
    {
        public const string MaxAge = "max_age";
        public const string Public = "public";
        public const string Private = "private";
        public const string MustRevalidate = "must_revalidate";
        public const string NoCache = "no_cache";
        public const string NoStore = "no_store";
        public const string MustUnderstand = "must_understand";
        public const string StaleWhileRevalidate = "stale_while_revalidate";
        public const string StaleIfError = "stale_if_error";
        public const string Immutable = "immutable";
    }

    private readonly Dictionary<string, object?> _values = new( StringComparer.Ordinal );

    public bool IsEmpty => _values.Count == 0 && Extras.Count == 0;

    public List<string> Extras { get; } = [];

    public long? MaxAge
    {
        get => _values.GetValueOrDefault( Keys.MaxAge ) as long?;
        set => _values[Keys.MaxAge] = value;
    }

    public bool? Public
    {
        get => _values.GetValueOrDefault( Keys.Public ) as bool?;
        set => _values[Keys.Public] = value;
    }

    public bool? Private
    {
        get => _values.GetValueOrDefault( Keys.Private ) as bool?;
        set => _values[Keys.Private] = value;
    }

    public bool? MustRevalidate
    {
        get => _values.GetValueOrDefault( Keys.MustRevalidate ) as bool?;
        set => _values[Keys.MustRevalidate] = value;
    }

    public bool? NoCache
    {
        get => _values.GetValueOrDefault( Keys.NoCache ) as bool?;
        set => _values[Keys.NoCache] = value;
    }

    public bool? NoStore
    {
        get => _values.GetValueOrDefault( Keys.NoStore ) as bool?;
        set => _values[Keys.NoStore] = value;
    }

    public bool? MustUnderstand
    {
        get => _values.GetValueOrDefault( Keys.MustUnderstand ) as bool?;
        set => _values[Keys.MustUnderstand] = value;
    }

    public long? StaleWhileRevalidate
    {
        get => _values.GetValueOrDefault( Keys.StaleWhileRevalidate ) as long?;
        set => _values[Keys.StaleWhileRevalidate] = value;
    }

    public long? StaleIfError
    {
        get => _values.GetValueOrDefault( Keys.StaleIfError ) as long?;
        set => _values[Keys.StaleIfError] = value;
    }

    public bool? Immutable
    {
        get => _values.GetValueOrDefault( Keys.Immutable ) as bool?;
        set => _values[Keys.Immutable] = value;
    }

    /// <summary><c>cache_control.delete(key)</c>, with Rails' directive names (<c>no_store</c>, <c>max_age</c>).</summary>
    public void Remove( string key ) => _values.Remove( key );

    /// <summary><c>cache_control.replace({})</c>.</summary>
    public void Clear( )
    {
        _values.Clear( );
        Extras.Clear( );
    }

    /// <summary>
    /// <c>merge_and_normalize_cache_control!</c>: an explicit <c>Cache-Control</c> header parsed into
    /// directives, merged with <paramref name="settings"/> (which drop the header's <c>no-cache</c> and
    /// <c>no-store</c>), then written back in Rails' order. <c>null</c> when both are empty (the
    /// <c>Rack::ETag</c> middleware then decides).
    /// </summary>
    public static string? Normalize( string? header, CacheControl settings )
    {
        ArgumentNullException.ThrowIfNull( settings );

        var control = Parse( header );
        if ( control.IsEmpty && settings.IsEmpty )
        {
            return null;
        }

        if ( !settings.IsEmpty )
        {
            control.Remove( Keys.NoCache );
            control.Remove( Keys.NoStore );
            var extras = settings.Extras.Concat( control.Extras ).Distinct( StringComparer.Ordinal ).ToList( );
            foreach ( var (key, value) in settings._values )
            {
                control._values[key] = value;
            }

            control.Extras.Clear( );
            control.Extras.AddRange( extras );
        }

        return string.Join( ", ", control.Directives( ) );
    }

    /// <summary><c>cache_control_headers</c>: a header's segments as directives, unknown ones as extras.</summary>
    internal static CacheControl Parse( string? header )
    {
        var control = new CacheControl( );
        if ( header is null )
        {
            return control;
        }

        foreach ( var segment in header.Replace( " ", "", StringComparison.Ordinal ).Split( ',' ) )
        {
            if ( segment.Length > 0 && !control.ApplySpecial( segment ) )
            {
                control.Extras.Add( segment );
            }
        }

        return control;
    }

    /// <summary><c>SPECIAL_KEYS</c>: directives that become hash keys (<c>true</c>, or their argument).</summary>
    private bool ApplySpecial( string segment )
    {
        var equals = segment.IndexOf( '=', StringComparison.Ordinal );
        var directive = equals < 0 ? segment : segment[..equals];
        if ( directive is not ("no-store" or "no-cache" or "max-age" or "public" or "private" or "must-revalidate" or "must-understand") )
        {
            return false;
        }

        _values[directive.Replace( '-', '_' )] = equals < 0 ? true : DirectiveArgumentInteger( segment[(equals + 1)..] );
        return true;
    }

    private bool IsSet( string key ) => _values.GetValueOrDefault( key ) is not (null or false);

    private List<string> Directives( )
    {
        var options = new List<string>( );
        if ( IsSet( Keys.NoStore ) )
        {
            AddIf( options, IsSet( Keys.Private ), "private" );
            AddIf( options, IsSet( Keys.MustUnderstand ), "must-understand" );
            options.Add( "no-store" );
            return options;
        }

        if ( IsSet( Keys.NoCache ) )
        {
            AddIf( options, IsSet( Keys.Public ), "public" );
            options.Add( "no-cache" );
            options.AddRange( Extras );
            return options;
        }

        AddIf( options, IsSet( Keys.MaxAge ), $"max-age={IntegerValue( Keys.MaxAge )}" );
        options.Add( IsSet( Keys.Public ) ? "public" : "private" );
        AddIf( options, IsSet( Keys.MustRevalidate ), "must-revalidate" );
        AddIf( options, IsSet( Keys.StaleWhileRevalidate ), $"stale-while-revalidate={IntegerValue( Keys.StaleWhileRevalidate )}" );
        AddIf( options, IsSet( Keys.StaleIfError ), $"stale-if-error={IntegerValue( Keys.StaleIfError )}" );
        AddIf( options, IsSet( Keys.Immutable ), "immutable" );
        options.AddRange( Extras );

        return options;
    }

    /// <summary>Ruby's <c>.to_i</c> on a stored value: <c>true.to_i</c> would raise, so a bare directive is 0.</summary>
    private long IntegerValue( string key ) => _values.GetValueOrDefault( key ) is long number ? number : 0;

    private static void AddIf( List<string> options, bool condition, string value )
    {
        if ( condition )
        {
            options.Add( value );
        }
    }

    /// <summary>
    /// The integer a directive's argument starts with: an optional sign and ASCII digits; 0 when there are none or
    /// they overflow a <see cref="long"/>. Rails stores the argument and applies Ruby's <c>String#to_i</c> when it writes
    /// the header, which also skips leading whitespace (spaces are already gone here), accepts underscores between
    /// digits and never overflows. This narrower reading is kept on purpose instead of
    /// <c>RailsIntegers.ParseLeadingInteger</c> (which saturates): no test covers those inputs, and the headers this
    /// port emits stay exactly as they were.
    /// </summary>
    private static long DirectiveArgumentInteger( string argument )
    {
        var end = argument.Length > 0 && argument[0] is '-' or '+' ? 1 : 0;
        while ( end < argument.Length && char.IsAsciiDigit( argument[end] ) )
        {
            end++;
        }

        return long.TryParse( argument.AsSpan( 0, end ), out var value ) ? value : 0;
    }
}
