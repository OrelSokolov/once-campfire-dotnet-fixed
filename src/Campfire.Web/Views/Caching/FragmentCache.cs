using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Globalization;
using Campfire.Application.Security;
using Campfire.Web.Views.Rendering;
using FragmentEntries = Campfire.Web.Views.Caching.ChargedLruTable<string, object>;

namespace Campfire.Web.Views.Caching;

/// <summary>
/// The process's fragment store: <c>cache record do ... end</c> in ERB (Rust <c>fragment_cache.rs</c>). Rails keeps
/// fragments in Redis, which grows without bound; this store is bounded by bytes the way Rails bounds its in-process
/// <c>ActiveSupport::Cache::MemoryStore</c>: each entry counts its key, its payload and
/// <see cref="PerEntryOverhead"/>, and when a write takes the total past the limit, least recently used entries go
/// until it is back to three quarters of the limit (<c>MemoryStore#prune</c>). Reads count as uses. Keys and
/// HTML payloads retain UTF-8 bytes and parsed token slots; other values keep their declared UTF-16 charge. Entries
/// live in per-generation namespaces (<c>FragmentCache.Namespaces.cs</c>).
/// </summary>
public sealed partial class FragmentCache : IDisposable
{
    /// <summary><c>MemoryStore</c>'s default size, 32 MB.</summary>
    public const long DefaultMaxBytes = 32L * 1024 * 1024;

    /// <summary>What an entry costs beyond its key and payload (<c>MemoryStore::PER_ENTRY_OVERHEAD</c>).</summary>
    public const long PerEntryOverhead = 240;

    /// <summary>The longest qualified key a lookup spells on the stack.</summary>
    private const int StackKeyCapacity = 512;

    private readonly State _state;
    private readonly string _prefix = string.Empty;
    private readonly bool _ownsState;
    private readonly long? _generation;

    public FragmentCache( long maxBytes = DefaultMaxBytes, TimeProvider? clock = null, SignedGlobalIds? identifiers = null )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( maxBytes );

        MaxBytes = maxBytes;
        _ownsState = true;
        _state = new State( );
        _state.Clock = clock ?? TimeProvider.System;
        _state.Identifiers = identifiers;
        _state.Meter.CreateObservableGauge( "campfire.fragments.renders", ( ) => Interlocked.Read( ref _state.Renders ) );
        _state.Meter.CreateObservableGauge( "campfire.fragments.charged_bytes", ( ) => Bytes );
        _state.Meter.CreateObservableGauge( "campfire.fragments.dropped_lookups", ( ) => Interlocked.Read( ref _state.DroppedLookups ) );
        _state.Meter.CreateObservableGauge( "campfire.fragments.lookups", ( ) => Interlocked.Read( ref _state.Lookups ) );
        _state.Meter.CreateObservableGauge( "campfire.fragments.dropped_stores", ( ) => Interlocked.Read( ref _state.DroppedStores ) );
        _state.Meter.CreateObservableGauge( "campfire.fragments.superseded_renders", ( ) => Interlocked.Read( ref _state.SupersededRenders ) );
    }

    private FragmentCache( FragmentCache store, long generation, string origin )
    {
        _state = store._state;
        _generation = generation;
        MaxBytes = store.MaxBytes;
        _prefix = string.Create( CultureInfo.InvariantCulture, $"{generation}:{origin.Length}:{origin}:" );
    }

    public long RenderCount => Interlocked.Read( ref _state.Renders );

    public long MaxBytes { get; }

    public int Count
    {
        get
        {
            lock ( Gate )
            {
                return _state.Unscoped.Entries.Count + _state.Ring.Sum( space => space?.Entries.Count ?? 0 );
            }
        }
    }

    /// <summary>The bytes the entries account for.</summary>
    public long Bytes
    {
        get
        {
            lock ( Gate )
            {
                return _state.Bytes;
            }
        }
    }

    private Lock Gate => _state.Gate;

    public void Dispose( )
    {
        if ( _ownsState )
        {
            _state.Dispose( );
        }
    }

    /// <summary>What an entry for <paramref name="key"/> with a payload of <paramref name="payloadChars"/> characters costs.</summary>
    public static long EntrySize( string key, long payloadChars ) => (key.Length + payloadChars) * sizeof( char ) + PerEntryOverhead;

    /// <summary>The fragment <paramref name="key"/> holds, if any (a use, for eviction).</summary>
    public bool TryGet( string key, [NotNullWhen( true )] out CachedFragment? html ) => TryGetValue( key, out html );

    /// <summary>
    /// The fragment a key spelled on the stack holds, with the key string it is stored under (so a hit allocates no
    /// key), if any (a use, for eviction).
    /// </summary>
    internal bool TryGet( ReadOnlySpan<char> key, [NotNullWhen( true )] out CachedFragment? html, [NotNullWhen( true )] out string? storedKey )
    {
        Interlocked.Increment( ref _state.Lookups );
        if ( _prefix.Length == 0 )
        {
            return TryGetQualified( key, out html, out storedKey );
        }

        var length = checked(_prefix.Length + key.Length);
        char[]? rental = length > StackKeyCapacity ? ArrayPool<char>.Shared.Rent( length ) : null;
        Span<char> qualified = rental is null ? stackalloc char[StackKeyCapacity] : rental;
        try
        {
            _prefix.AsSpan( ).CopyTo( qualified );
            key.CopyTo( qualified[_prefix.Length..] );
            return TryGetQualified( qualified[..length], out html, out storedKey );
        }
        finally
        {
            if ( rental is not null )
            {
                qualified[..length].Clear( );
                ArrayPool<char>.Shared.Return( rental );
            }
        }
    }

    /// <summary>The value <paramref name="key"/> holds, if it is a <typeparamref name="T"/>.</summary>
    public bool TryGetValue<T>( string key, [NotNullWhen( true )] out T? value )
        where T : class
    {
        ArgumentNullException.ThrowIfNull( key );
        Interlocked.Increment( ref _state.Lookups );
        key = Qualify( key );

        lock ( Gate )
        {
            if ( LookupNamespace( ) is { } space && space.Entries.Find( key ) is { Value.Value: T stored } node && TryUse( space, node ) )
            {
                value = stored;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary><c>Rails.cache.fetch(key) { render }</c> for a rendered fragment.</summary>
    public CachedFragment Fetch( string key, Func<SafeHtml> render )
    {
        ArgumentNullException.ThrowIfNull( render );

        if ( TryGet( key, out var cached ) )
        {
            return cached;
        }

        Interlocked.Increment( ref _state.Renders );
        var html = render( );
        RenderFinished( );
        return Store( key, html );
    }

    /// <summary>
    /// <see cref="Fetch"/> for a render that awaits (slices render asynchronously) to UTF-8 directly: the store takes
    /// the owned array rather than round-tripping through a string. Rendered unlocked, since a fragment renders the
    /// fragments nested in it through this store.
    /// </summary>
    internal async ValueTask<CachedFragment> FetchUtf8Async( string key, Func<ValueTask<byte[]>> render, DateTimeOffset? validUntil = null )
    {
        ArgumentNullException.ThrowIfNull( render );

        if ( TryGet( key, out var cached ) )
        {
            return cached;
        }

        Interlocked.Increment( ref _state.Renders );
        var bytes = await render( );
        RenderFinished( );
        return StoreFragment( key, new CachedFragment( bytes ), validUntil );
    }

    /// <summary><c>Rails.cache.fetch(key) { value }</c> for any value (Jbuilder caches the hash it built, not its JSON).</summary>
    public T FetchValue<T>( string key, Func<T> compute, Func<T, long> payloadChars )
        where T : class
    {
        ArgumentNullException.ThrowIfNull( compute );
        ArgumentNullException.ThrowIfNull( payloadChars );

        if ( TryGetValue<T>( key, out var cached ) )
        {
            return cached;
        }

        Interlocked.Increment( ref _state.Renders );
        var value = compute( );
        RenderFinished( );
        key = Qualify( key );
        return Write( key, value, EntrySize( key, payloadChars( value ) ) );
    }

    /// <summary>Stores <paramref name="html"/> unless <paramref name="key"/> already holds a fragment, and returns what the key holds.</summary>
    public CachedFragment Store( string key, SafeHtml html, DateTimeOffset? validUntil = null )
    {
        ArgumentNullException.ThrowIfNull( key );
        ArgumentNullException.ThrowIfNull( html );

        return StoreFragment( key, new CachedFragment( html ), validUntil );
    }

    public void Clear( )
    {
        lock ( Gate )
        {
            _state.Unscoped.Entries.Clear( );
            foreach ( var space in _state.Ring )
            {
                space?.Entries.Clear( );
            }

            _state.Bytes = 0;
        }
    }

    private string Qualify( string key ) => _prefix.Length == 0 ? key : string.Concat( _prefix, key );

    private bool TryGetQualified( ReadOnlySpan<char> key, [NotNullWhen( true )] out CachedFragment? html, [NotNullWhen( true )] out string? storedKey )
    {
        lock ( Gate )
        {
            if ( LookupNamespace( ) is { } space && space.Entries.Find( key ) is { Value.Value: CachedFragment fragment } node && TryUse( space, node ) )
            {
                html = fragment;
                storedKey = node.Value.Key;
                return true;
            }
        }

        html = null;
        storedKey = null;
        return false;
    }

    /// <summary>Under the lock: the namespace a lookup reads, counting a lookup whose namespace a newer generation dropped.</summary>
    private Namespace? LookupNamespace( )
    {
        var space = CurrentNamespace( );
        if ( space is null )
        {
            Interlocked.Increment( ref _state.DroppedLookups );
        }

        return space;
    }

    /// <summary>
    /// Under the lock: uses a found entry, making it the most recently used, unless it is a fragment past its
    /// freshness limit, which is evicted instead.
    /// </summary>
    private bool TryUse( Namespace space, LinkedListNode<FragmentEntries.Entry> node )
    {
        if ( node.Value.Value is CachedFragment fragment && fragment.ValidUntil <= _state.Clock.GetUtcNow( ) )
        {
            Evict( space, node );
            return false;
        }

        space.Entries.Touch( node );
        return true;
    }

    private void RenderFinished( )
    {
        if ( _generation is { } generation && generation < Interlocked.Read( ref _state.Generation ) )
        {
            Interlocked.Increment( ref _state.SupersededRenders );
        }
    }

    private CachedFragment StoreFragment( string key, CachedFragment fragment, DateTimeOffset? validUntil )
    {
        fragment.ValidUntil = validUntil ?? DateTimeOffset.MaxValue;
        if ( _state.Identifiers is { } identifiers )
        {
            fragment.ValidUntil = PageClockFreshness.Limit( fragment.Bytes, identifiers, _state.Clock.GetUtcNow( ), fragment.ValidUntil );
        }

        key = Qualify( key );
        return Write( key, fragment, key.Length * sizeof( char ) + fragment.RetainedBytes + PerEntryOverhead );
    }

    /// <summary>
    /// Stores <paramref name="value"/> unless <paramref name="key"/> already holds one of its type; when two renders of
    /// a key race, the first one stored is what both return.
    /// </summary>
    private T Write<T>( string key, T value, long size )
        where T : class
    {
        if ( value is CachedFragment fragment && fragment.ValidUntil <= _state.Clock.GetUtcNow( ) )
        {
            return value;
        }

        lock ( Gate )
        {
            var space = CurrentNamespace( );
            if ( space is null )
            {
                Interlocked.Increment( ref _state.DroppedStores );
                return value;
            }

            if ( space.Entries.Find( key ) is { } existing )
            {
                if ( existing.Value.Value is T stored )
                {
                    space.Entries.Touch( existing );
                    return stored;
                }

                Evict( space, existing );
            }

            space.Entries.Add( key, value, size );
            _state.Bytes += size;
            if ( _state.Bytes > MaxBytes )
            {
                // MemoryStore#prune(@max_size * 0.75)
                Prune( MaxBytes / 4 * 3 );
            }

            return value;
        }
    }

    private void Evict( Namespace space, LinkedListNode<FragmentEntries.Entry> node )
    {
        space.Entries.Remove( node );
        _state.Bytes -= node.Value.Charge;
    }

    /// <summary>What every scope of one store shares: the lock, the namespaces, the charge and the counters.</summary>
    private sealed class State : IDisposable
    {
        internal TimeProvider Clock = TimeProvider.System;
        internal SignedGlobalIds? Identifiers;
        internal readonly Lock Gate = new( );
        internal readonly Namespace Unscoped = new( 0 );
        internal readonly Namespace?[] Ring = new Namespace?[RetainedGenerations];
        internal readonly Meter Meter = new( "Campfire.Fragments" );
        internal long Bytes;
        internal long Renders;
        internal long Generation;
        internal long DroppedLookups;
        internal long Lookups;
        internal long DroppedStores;
        internal long SupersededRenders;

        public void Dispose( ) => Meter.Dispose( );
    }
}
