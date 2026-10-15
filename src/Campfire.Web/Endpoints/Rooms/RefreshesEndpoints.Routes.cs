using System.Globalization;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Rooms;

public static partial class RefreshesEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapMethods( EndpointPaths.RoomsRefreshes.Refresh, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

    private static async Task<FrontendResult> HandleShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        [FromQuery] string? since,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var timestamp = long.TryParse( since, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed )
            ? parsed
            : OverflowOrEpoch( since );
        requestContext.SetCurrent( new RefreshQuery( timestamp ) );
        var rejection = await ResourceAccess.SetRoom( requestContext, roomId )
            ?? await SetLastUpdatedAtAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await ShowAsync( requestContext );
    }

    private static long OverflowOrEpoch( string? since )
    {
        var text = since.AsSpan( ).Trim( );
        var negative = text.StartsWith( "-", StringComparison.Ordinal );
        if ( negative || text.StartsWith( "+", StringComparison.Ordinal ) )
        {
            text = text[1..];
        }

        return !text.IsEmpty && text.IndexOfAnyExceptInRange( '0', '9' ) < 0
            ? negative ? long.MinValue : long.MaxValue
            : 0;
    }
}
