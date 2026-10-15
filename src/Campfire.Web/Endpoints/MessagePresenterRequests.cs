using Campfire.Web.App;
using Campfire.Web.Http;

namespace Campfire.Web.Endpoints;

/// <summary>How an endpoint gets its request's <see cref="MessagePresenters"/>.</summary>
public static class MessagePresenterRequests
{
    /// <summary>The request's message presenters, once ffmpeg's presence (for video previews) is known.</summary>
    public static async ValueTask<MessagePresenters> MessagePresentersAsync( this RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var app = requestContext.App( );
        var ffmpegExists = await app.Storage.Tools.FfmpegExistsAsync( );
        return new MessagePresenters( app, requestContext, ffmpegExists );
    }

    /// <summary>The request's message presenters for output without video previews: JSON, and a boost's partial.</summary>
    public static MessagePresenters MessagePresentersWithoutPreviews( this RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        return new MessagePresenters( requestContext.App( ), requestContext, ffmpegExists: false );
    }
}
