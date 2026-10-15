using System.Text;

namespace Campfire.Web.Http;

/// <summary>
/// <c>ActionDispatch::RequestId</c>: the incoming <c>X-Request-Id</c> stripped to <c>[\w\-@]</c> and 255
/// characters, or a fresh UUID; sent back on the response and kept for logging.
/// </summary>
public sealed class RequestIdMiddleware( RequestDelegate next )
{
    private const int MaximumRequestIdLength = 255;

    public Task InvokeAsync( HttpContext httpContext )
    {
        Apply( httpContext );
        return next( httpContext );
    }

    internal static void Apply( HttpContext httpContext )
    {
        ArgumentNullException.ThrowIfNull( httpContext );

        var requestId = MakeRequestId( httpContext.Request.Headers[CampfireHeaderNames.RequestId].ToString( ) );
        httpContext.TraceIdentifier = requestId;
        httpContext.Response.OnStarting( ( ) =>
        {
            httpContext.Response.Headers[CampfireHeaderNames.RequestId] = requestId;
            return Task.CompletedTask;
        } );
    }

    /// <summary><c>make_request_id</c>.</summary>
    public static string MakeRequestId( string? incoming )
    {
        if ( string.IsNullOrWhiteSpace( incoming ) )
        {
            return Guid.NewGuid( ).ToString( );
        }

        var id = new StringBuilder( );
        foreach ( var character in incoming )
        {
            if ( id.Length == MaximumRequestIdLength )
            {
                break;
            }

            if ( char.IsAsciiLetterOrDigit( character ) || character is '_' or '-' or '@' )
            {
                id.Append( character );
            }
        }

        return id.ToString( );
    }
}
