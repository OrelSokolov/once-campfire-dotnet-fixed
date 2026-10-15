using System.Globalization;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;

namespace Campfire.Web.Presenters;

/// <summary>
/// geared_pagination 1.2.0's <c>set_page_and_extract_portion_from records, per_page:</c> without <c>ordered_by</c>
/// (<c>Recordset</c> with a <c>PortionAtOffset</c>): the page number from <c>params[:page]</c>, and the page's
/// <c>LIMIT</c>, <c>OFFSET</c>, count of pages and next parameter. The records are paged in SQL by the caller; the
/// action stores the result with <see cref="Store"/>, as Rails keeps <c>@page</c> for the pagination etagger and
/// <c>set_paginated_headers</c>.
/// </summary>
public sealed class GearedPortion
{
    private readonly long[] _ratios;

    public GearedPortion( long number, IReadOnlyList<long> perPage )
    {
        ArgumentNullException.ThrowIfNull( perPage );
        if ( perPage.Count == 0 || perPage.Any( ratio => ratio < 1 ) )
        {
            throw new ArgumentException( "A page needs at least one ratio, and every ratio must be positive.", nameof( perPage ) );
        }

        Number = number;
        _ratios = [.. perPage];
    }

    /// <summary><c>page.number</c>.</summary>
    public long Number { get; }

    /// <summary><c>PortionAtOffset#limit</c>: the ratio for this page.</summary>
    public long Limit => Ratio( Number );

    /// <summary>
    /// <c>PortionAtOffset#offset</c>: the variable ratios of the earlier pages, then the fixed one. Ruby's offset grows
    /// into a Bignum; this one saturates at <see cref="long.MaxValue"/> (saturating page arithmetic), which pages past every row, where a
    /// wrapped product would go negative and SQLite would read it as offset 0, the first page.
    /// </summary>
    public long Offset
    {
        get
        {
            var variable = 0L;
            for ( var index = 0L; index < Math.Min( Number - 1, _ratios.Length - 1 ); index++ )
            {
                variable += Ratio( index + 1 );
            }

            var fixedPages = Math.Max( Number - _ratios.Length, 0 );
            if ( fixedPages > (long.MaxValue - variable) / _ratios[^1] )
            {
                return long.MaxValue;
            }

            return variable + (fixedPages * _ratios[^1]);
        }
    }

    /// <summary><c>page.next_param</c>: the next page's number, saturated as the page number is (saturating page arithmetic).</summary>
    public string NextParam => (Number == long.MaxValue ? Number : Number + 1).ToString( CultureInfo.InvariantCulture );

    /// <summary><c>page.cache_key</c>: <c>"page/#{page_number}:#{ratios.join('-')}"</c>.</summary>
    public string CacheKey => $"page/{Number.ToString( CultureInfo.InvariantCulture )}:{string.Join( '-', _ratios )}";

    /// <summary>
    /// <c>Recordset#page(params[:page])</c>: <c>param.to_i &gt; 0 ? param.to_i : 1</c>. A value without <c>to_i</c> (a
    /// hash, an array, a boolean) raises <c>NoMethodError</c> in Rails, a 500.
    /// </summary>
    public static GearedPortion From( InputValue? page, params IReadOnlyList<long> perPage )
    {
        var number = page switch
        {
            null or InputNull => 0,
            InputText text => RailsIntegers.ParseLeadingInteger( text.Value ),
            InputNumber json => RailsIntegers.ParseLeadingInteger( json.ToText( ) ),
            _ => throw new InvalidOperationException( $"undefined method 'to_i' for {page.GetType( ).Name}" ),
        };

        return new GearedPortion( number > 0 ? number : 1, perPage );
    }

    /// <summary><c>recordset.page_count</c>: pages until the ratios cover every record, at least one.</summary>
    public long PageCount( long recordsCount )
    {
        var count = 0L;
        var residual = recordsCount;
        while ( residual > 0 )
        {
            count++;
            residual -= Ratio( count );
        }

        return count > 0 ? count : 1;
    }

    /// <summary><c>page.last?</c>.</summary>
    public bool IsLast( long recordsCount ) => Number == PageCount( recordsCount );

    /// <summary>
    /// <c>@page = ...</c>: the page the pagination etagger and <c>set_paginated_headers</c> read, for a relation of
    /// <paramref name="recordsCount"/> records.
    /// </summary>
    public GearedPage Store( RequestContext requestContext, long recordsCount )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var page = new GearedPage( recordsCount, IsLast( recordsCount ), NextParam, CacheKey );
        requestContext.SetCurrent( page );
        return page;
    }

    /// <summary><c>Ratios#[]</c>: the page's ratio, or the last one past the list.</summary>
    private long Ratio( long pageNumber ) => pageNumber >= 1 && pageNumber <= _ratios.Length ? _ratios[pageNumber - 1] : _ratios[^1];
}
