using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>
/// A response as a controller builds it, before the HTTP layer finishes it (the flash committed into its
/// cookie, the request's response headers, <c>Cache-Control</c>, <c>Rack::ETag</c>, <c>Rack::ConditionalGet</c>
/// and <c>Rack::Head</c>'s empty body). Returning one from an action or a before-action hands it to
/// <see cref="FrontendResultWriter"/> through <see cref="IResult"/>.
/// </summary>
public sealed class FrontendResult : IResult
{
    public FrontendResult( int status )
    {
        Status = status;
    }

    public int Status { get; set; }

    /// <summary>Response headers, case-insensitive; <c>Set-Cookie</c> may hold several values.</summary>
    public IHeaderDictionary Headers { get; } = new HeaderDictionary( );

    public ResponseBody Body { get; set; } = ResponseBody.Empty;

    /// <summary>
    /// Built with <c>ActionController::Live</c> semantics (<c>ActiveStorage::Streaming</c>): no default
    /// headers, never digested by <c>Rack::ETag</c>.
    /// </summary>
    public bool Live { get; set; }

    public string? ContentType => Header( HeaderNames.ContentType );

    public string? Location => Header( HeaderNames.Location );

    /// <summary>A response with a body and a content type.</summary>
    public static FrontendResult WithBody( int status, string contentType, ResponseBody body )
    {
        var response = new FrontendResult( status ) { Body = body };
        response.Headers.ContentType = contentType;

        return response;
    }

    /// <summary>A response with a UTF-8 text body.</summary>
    public static FrontendResult WithBody( int status, string contentType, string body ) =>
        WithBody( status, contentType, ResponseBody.FromString( body ) );

    /// <summary>Set (replace) a header and return this response.</summary>
    public FrontendResult Header( string name, string value )
    {
        Headers[name] = value;
        return this;
    }

    /// <summary>The first value of a header, or <c>null</c>.</summary>
    public string? Header( string name ) => Headers.TryGetValue( name, out var values ) && values.Count > 0 ? values[0] : null;

    public Task ExecuteAsync( HttpContext httpContext )
    {
        ArgumentNullException.ThrowIfNull( httpContext );

        return FrontendResultWriter.ExecuteAsync( httpContext, this );
    }
}
