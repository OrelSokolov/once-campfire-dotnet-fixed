using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Views.Models;

/// <summary>
/// A partial whose whole body is <c>cache record do ... end</c>, on its way into a page or a broadcast: the fragment
/// itself when the store already holds this version, else the view to render (and store) it from. The fragment is
/// looked up before the view is built, so on a hit nothing of the partial is evaluated.
/// </summary>
/// <typeparam name="TView">What the partial renders from.</typeparam>
public abstract record CachedItem<TView>
    where TView : class
{
    private protected CachedItem( CachedFragment? fragment, TView? view, string? cacheKey )
    {
        Fragment = fragment;
        View = view;
        CacheKey = cacheKey;
    }

    /// <summary>The stored fragment on a hit.</summary>
    public CachedFragment? Fragment { get; }

    /// <summary>The view to render on a miss.</summary>
    public TView? View { get; private protected set; }

    /// <summary>Where a rendered view is stored; <see langword="null"/> renders uncached.</summary>
    public string? CacheKey { get; }

    /// <summary>
    /// Looks up the fragment stored under <paramref name="key"/>: on a hit it returns the fragment, with the key it is
    /// stored under; on a miss <see langword="null"/>, with the key a rendered view is stored under.
    /// </summary>
    private protected static CachedFragment? Find( FragmentCache cache, ReadOnlySpan<char> key, out string cacheKey )
    {
        if ( cache.TryGet( key, out var fragment, out var storedKey ) )
        {
            cacheKey = storedKey;
            return fragment;
        }

        cacheKey = new string( key );
        return null;
    }
}

/// <summary>A message on its way into <c>messages/_message</c>, whose body is <c>cache [ message, ... ]</c>.</summary>
public sealed record MessageItem : CachedItem<MessageView>
{
    private MessageItem( string clientMessageId, long roomId, CachedFragment? fragment, MessageView? view, string? cacheKey )
        : base( fragment, view, cacheKey )
    {
        ClientMessageId = clientMessageId;
        RoomId = roomId;
    }

    public string ClientMessageId { get; }

    public long RoomId { get; }

    /// <summary>An uncached message (tests, previews, or a store-less context).</summary>
    public static MessageItem Uncached( MessageView view )
    {
        ArgumentNullException.ThrowIfNull( view );

        return new( view.ClientMessageId, view.RoomId, null, view, null );
    }

    /// <summary>
    /// Looks the fragment up before building the view: on a hit <paramref name="build"/> never runs.
    /// </summary>
    public static MessageItem Lookup( ViewContext viewContext, MessageCacheInputs inputs, string clientMessageId, long roomId, Func<MessageView> build )
    {
        ArgumentNullException.ThrowIfNull( build );

        return Lookup( viewContext, inputs, clientMessageId, roomId, build, static factory => factory( ) );
    }

    /// <summary>The factory's state is passed without allocating a closure on fragment-cache hits.</summary>
    internal static MessageItem Lookup<TState>(
        ViewContext viewContext,
        MessageCacheInputs inputs,
        string clientMessageId,
        long roomId,
        TState state,
        Func<TState, MessageView> build )
    {
        ArgumentNullException.ThrowIfNull( build );

        var item = Find( viewContext, inputs, clientMessageId, roomId );
        if ( item.Fragment is null )
        {
            item.View = build( state );
        }

        return item;
    }

    /// <summary>
    /// Looks the fragment up without building anything: on a hit the item holds the fragment; on a miss it holds
    /// only the key, and its view comes from <see cref="Build"/> (so a page can load what all its misses need first).
    /// </summary>
    internal static MessageItem Find( ViewContext viewContext, MessageCacheInputs inputs, string clientMessageId, long roomId )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        if ( viewContext.Cache is not { } cache )
        {
            return new( clientMessageId, roomId, null, null, null );
        }

        Span<char> key = stackalloc char[FragmentKeys.MessageKeyCapacity];
        var written = FragmentKeys.WriteMessageKey( inputs, key );
        var fragment = Find( cache, key[..written], out var cacheKey );
        return new( clientMessageId, roomId, fragment, null, cacheKey );
    }

    /// <summary>Gives an item <see cref="Find"/> missed the view it renders from.</summary>
    internal void Build( MessageView view )
    {
        ArgumentNullException.ThrowIfNull( view );

        if ( Fragment is not null || View is not null )
        {
            throw new InvalidOperationException( "Only a missed message item is built, once." );
        }

        View = view;
    }

    /// <summary><c>dom_id(message, prefix)</c>.</summary>
    public string DomId( string? prefix = null ) => DomIds.Message( ClientMessageId, prefix );
}

/// <summary>A direct room on its way into the sidebar: <c>users/sidebars/rooms/_direct</c>, whose body is <c>cache membership</c>.</summary>
public sealed record SidebarDirectItem : CachedItem<SidebarDirect>
{
    private SidebarDirectItem( CachedFragment? fragment, SidebarDirect? view, string? cacheKey )
        : base( fragment, view, cacheKey )
    {
    }

    public static SidebarDirectItem Uncached( SidebarDirect view )
    {
        ArgumentNullException.ThrowIfNull( view );

        return new( null, view, null );
    }

    /// <summary>Looks the fragment up before building the view.</summary>
    public static SidebarDirectItem Lookup(
        ViewContext viewContext,
        RecordVersion membership,
        RecordVersion room,
        IReadOnlyList<RecordVersion> members,
        Func<SidebarDirect> build )
    {
        ArgumentNullException.ThrowIfNull( build );

        return Lookup( viewContext, membership, room, members, build, static factory => factory( ) );
    }

    /// <summary>Warm hits use a span key and explicit factory state without building a key string or closure.</summary>
    internal static SidebarDirectItem Lookup<TState>(
        ViewContext viewContext,
        RecordVersion membership,
        RecordVersion room,
        IReadOnlyList<RecordVersion> members,
        TState state,
        Func<TState, SidebarDirect> build )
    {
        ArgumentNullException.ThrowIfNull( viewContext );
        ArgumentNullException.ThrowIfNull( members );
        ArgumentNullException.ThrowIfNull( build );

        if ( viewContext.Cache is not { } cache )
        {
            return Uncached( build( state ) );
        }

        Span<char> key = stackalloc char[FragmentKeys.SidebarDirectKeyCapacity];
        var written = FragmentKeys.WriteSidebarDirectKey( membership, room, members, key );
        var fragment = Find( cache, key[..written], out var cacheKey );
        return fragment is not null ? new( fragment, null, cacheKey ) : new( null, build( state ), cacheKey );
    }
}
