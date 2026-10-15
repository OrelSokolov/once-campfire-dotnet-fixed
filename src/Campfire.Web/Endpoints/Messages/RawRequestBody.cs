using System.Text;
using Campfire.Web.Http;

namespace Campfire.Web.Endpoints.Messages;

/// <summary>
/// <c>RawRequestBody</c> (<c>app/controllers/concerns/raw_request_body.rb</c>): the bot API takes a message or a boost
/// as the whole request body, whatever its content type (<c>request.body.read.force_encoding("UTF-8")</c>).
/// </summary>
public static class RawRequestBody
{
    private static readonly UTF8Encoding Utf8 = new( false, true );
    /// <summary><c>raw_request_body</c>.</summary>
    public static string Read( RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        try
        {
            return Utf8.GetString( requestContext.RawPost.Span );
        }
        catch ( DecoderFallbackException )
        {
            throw new BadHttpRequestException( "The message body must be valid UTF-8." );
        }
    }

    /// <summary><c>String#blank?</c>: empty or only Unicode whitespace.</summary>
    public static bool IsBlank( string text ) => string.IsNullOrWhiteSpace( text );
}
