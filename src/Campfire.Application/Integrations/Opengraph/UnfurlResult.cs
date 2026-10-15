namespace Campfire.Application.Integrations.Opengraph;

/// <summary>What <c>UnfurlLinksController#create</c> answers.</summary>
public abstract record UnfurlResult
{
    /// <summary><c>head :no_content</c> (204).</summary>
    public static UnfurlResult NoContent { get; } = new NoContentResult( );

    /// <summary><c>render json: opengraph</c> (200, <c>application/json; charset=utf-8</c>) with this body.</summary>
    public sealed record Json( string Body ) : UnfurlResult;

    private sealed record NoContentResult : UnfurlResult;
}
