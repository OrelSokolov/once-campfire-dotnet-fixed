namespace Campfire.Web.Http;

/// <summary>
/// A page from <c>set_page_and_extract_portion_from</c> (geared_pagination 1.2.0), which the action
/// stores with <see cref="RequestContext.SetCurrent{T}"/> as Rails stores <c>@page</c>. The pagination
/// etagger adds its cache key to <c>fresh_when</c>, and <c>set_paginated_headers</c> reads it.
/// </summary>
/// <param name="RecordsCount"><c>page.recordset.records_count</c>.</param>
/// <param name="IsLast"><c>page.last?</c>.</param>
/// <param name="NextParam"><c>page.next_param</c>.</param>
/// <param name="CacheKey"><c>page.cache_key</c>.</param>
public sealed record GearedPage( long RecordsCount, bool IsLast, string NextParam, string CacheKey );
