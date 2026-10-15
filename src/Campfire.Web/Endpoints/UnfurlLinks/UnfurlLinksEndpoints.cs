using Campfire.Application.Integrations.Opengraph;
using Campfire.Web.Http;

namespace Campfire.Web.Endpoints.UnfurlLinks;

/// <summary>
/// <c>UnfurlLinksController</c> (<c>app/controllers/unfurl_links_controller.rb</c>): the composer asks for the
/// Opengraph metadata of a pasted URL. <see cref="OpengraphUnfurler"/> fetches it through the private network guard
/// (every address and redirect checked, the resolved address pinned, 5 MB and 10 redirects at most, plus the port's
/// deadline and concurrency bounds), and the action answers the metadata as <c>render json:</c> writes it, or 204.
/// </summary>
public static partial class UnfurlLinksEndpoints
{
    /// <summary>
    /// <c>create</c>: <c>Opengraph::Metadata.from_url(params.require(:url))</c>, then <c>render json: opengraph</c> when
    /// it's valid, else <c>head :no_content</c>. A missing or blank <c>url</c> is <c>ParameterMissing</c> (400). A hash or
    /// an array passes <c>require</c>, but <c>URI.parse</c> can't take it (<c>InvalidURIError</c>, which the location
    /// rescues), so nothing is fetched and the metadata isn't valid: 204.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext, string? text )
    {
        if ( string.IsNullOrWhiteSpace( text ) )
        {
            throw new BadHttpRequestException( "A URL is required.", StatusCodes.Status400BadRequest );
        }

        var unfurler = requestContext.HttpContext.RequestServices.GetRequiredService<OpengraphUnfurler>( );
        var result = await unfurler.UnfurlAsync( text, requestContext.RequestAborted );

        return result switch
        {
            UnfurlResult.Json json => requestContext.RenderAs( StatusCodes.Status200OK, ContentTypes.JsonUtf8, ResponseBody.FromString( json.Body ) ),
            _ => requestContext.Head( StatusCodes.Status204NoContent ),
        };
    }
}
