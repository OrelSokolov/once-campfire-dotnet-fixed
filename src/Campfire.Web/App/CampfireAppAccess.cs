using Campfire.Application.Data;
using Campfire.Web.Http;
using Campfire.Web.Presenters;

namespace Campfire.Web.App;

/// <summary><c>requestContext.App( )</c> and friends: the app's shared services, reached from an action.</summary>
public static class CampfireAppAccess
{
    /// <summary>The app's shared services, from the request's container.</summary>
    public static CampfireApp App( this RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        return requestContext.HttpContext.RequestServices.GetRequiredService<CampfireApp>( );
    }

    /// <summary>The app's database, for <c>ReadAsync</c> and <c>WriteAsync</c>.</summary>
    public static CampfireDatabase Database( this RequestContext requestContext ) => requestContext.App( ).Database;

    /// <summary>Builds the request's (or a broadcast's) <see cref="Views.Models.ViewContext"/>.</summary>
    public static ViewContexts Views( this RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        return requestContext.HttpContext.RequestServices.GetRequiredService<ViewContexts>( );
    }
}
