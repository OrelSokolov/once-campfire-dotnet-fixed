using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Models;
using RazorSlices;

namespace Campfire.Web.Views.Rendering;

/// <summary>The store step of a partial whose whole body is <c>cache record do ... end</c>, shared by pages and broadcasts.</summary>
internal static class CachedPartials
{
    /// <summary>
    /// <c>Rails.cache.fetch(key) { render partial }</c>: the fragment stored under <paramref name="cacheKey"/>, else
    /// <paramref name="view"/> rendered by <paramref name="createSlice"/> in <paramref name="fragmentContext"/> (no
    /// viewer, no tokens) and stored.
    /// </summary>
    internal static ValueTask<CachedFragment> FetchRenderedAsync<TView>(
        this FragmentCache cache,
        string cacheKey,
        ViewContext fragmentContext,
        TView view,
        Func<ViewContext, TView, RazorSlice> createSlice,
        DateTimeOffset? validUntil,
        CancellationToken cancellationToken ) =>
        cache.FetchUtf8Async(
            cacheKey,
            ( ) => SliceRenderer.RenderToUtf8Async( createSlice( fragmentContext, view ), cancellationToken ),
            validUntil );
}
