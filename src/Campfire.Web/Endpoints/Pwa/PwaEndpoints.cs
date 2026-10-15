using Campfire.Application.Accounts;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Endpoints.Pwa;

/// <summary>
/// <c>PwaController</c> (<c>app/controllers/pwa_controller.rb</c>): the web app manifest and the service worker, at
/// stable URLs outside the digested assets.
/// </summary>
public static partial class PwaEndpoints
{
    /// <summary><c>pwa/service_worker.js</c>, served as it is.</summary>
    private static Task<FrontendResult> ServiceWorkerAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.Js );
        return Task.FromResult( requestContext.Render( StatusCodes.Status200OK, Mime.Js, Views.pwa.ServiceWorker.Script ) );
    }

    /// <summary><c>pwa/manifest.json.erb</c>: the account's name and logo.</summary>
    private static async Task<FrontendResult> ManifestAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.Json );
        var database = requestContext.Database( );
        var account = await database.ReadAsync( AccountQueries.First, requestContext.RequestAborted );
        var viewContext = await requestContext.Views( ).ForRequestAsync( requestContext );
        var model = new ManifestPage( viewContext, account?.Name, ViewContexts.LogoPath( account, "small" ), ViewContexts.LogoPath( account ) );

        var json = await SliceRenderer.RenderToStringAsync( Views.pwa.manifest_json.Create( model ), requestContext.RequestAborted );
        return requestContext.Render( StatusCodes.Status200OK, Mime.Json, json );
    }
}
