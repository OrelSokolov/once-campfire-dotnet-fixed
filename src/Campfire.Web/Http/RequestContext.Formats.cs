using Campfire.Web.Paths;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>Format negotiation (<c>request.formats</c>, <c>respond_to</c>, Turbo Frame requests).</summary>
public sealed partial class RequestContext
{
    private IReadOnlyList<MimeType>? _formats;
    private MimeType? _renderedFormat;

    /// <summary>
    /// <c>request.formats</c>, cached for the request. Throws <see cref="InvalidMimeTypeException"/>
    /// (406) for a malformed <c>Accept</c>, and <see cref="BadRequestException"/> when reading
    /// <c>params[:format]</c> fails on a malformed body (Rails then ignores the param).
    /// </summary>
    public IReadOnlyList<MimeType> Formats
    {
        get
        {
            _formats ??= FormatNegotiation.Formats( NegotiationInput( ) );

            return _formats;
        }
    }

    /// <summary><c>request.format</c>: the first format; <c>null</c> is <c>Mime::NullType</c>.</summary>
    public MimeType? Format => Formats.Count > 0 ? Formats[0] : null;

    /// <summary><c>turbo_frame_request?</c>.</summary>
    public bool IsTurboFrameRequest => !string.IsNullOrWhiteSpace( TurboFrameRequestId );

    /// <summary>The <c>Turbo-Frame</c> header.</summary>
    public string? TurboFrameRequestId => Request.Header( CampfireHeaderNames.TurboFrame );

    /// <summary>
    /// <c>respond_to do |format| ... end</c>: the first format the client accepts among
    /// <paramref name="offered"/> (in the order the block declares them). Throws
    /// <see cref="UnknownFormatException"/> (406) when none is acceptable.
    /// </summary>
    public MimeType RespondTo( params IReadOnlyList<MimeType> offered )
    {
        ArgumentNullException.ThrowIfNull( offered );

        var chosen = FormatNegotiation.Negotiate( Formats, offered ) ?? throw new UnknownFormatException( "ActionController::UnknownFormat" );
        if ( chosen == Mime.All )
        {
            chosen = offered.Count > 0 ? offered[0] : Mime.Html;
        }

        _renderedFormat = chosen;
        return chosen;
    }

    /// <summary>The format a render uses: the <c>respond_to</c> choice, else the first request format (<c>*/*</c> is HTML).</summary>
    public MimeType RenderedFormat( )
    {
        if ( _renderedFormat is not null )
        {
            return _renderedFormat;
        }

        MimeType? first;
        try
        {
            first = Format;
        }
        catch ( Exception error ) when ( error is InvalidMimeTypeException or BadRequestException )
        {
            first = null;
        }

        return first is null || first == Mime.All ? Mime.Html : first;
    }

    /// <summary>
    /// The format <c>PublicExceptions</c> renders an error in: <c>request.formats.first</c>, after
    /// <c>ShowExceptions#fallback_to_html_format_if_invalid_mime_type</c>.
    /// </summary>
    internal MimeType? ErrorFormat( )
    {
        try
        {
            return Format;
        }
        catch ( Exception error ) when ( error is InvalidMimeTypeException or BadRequestException )
        {
            return ErrorFormat( HttpContext, Request.Path );
        }
    }

    /// <summary><see cref="ErrorFormat()"/> for a request that never reached a Rails action.</summary>
    internal static MimeType? ErrorFormat( HttpContext httpContext, string path )
    {
        var headers = httpContext.Request.Headers;
        var format = httpContext.Request.Query[ParamNames.Format].ToString( );
        var contentType = headers.ContentType.ToString( ) is { Length: > 0 } type ? type : null;
        var input = new NegotiationInput( format, headers.Accept.ToString( ), contentType, path, FrontendRequest.IsXmlHttpRequest( headers ) );
        try
        {
            return FormatNegotiation.Formats( input ) is { Count: > 0 } formats ? formats[0] : null;
        }
        catch ( InvalidMimeTypeException )
        {
            var fallback = input with
            {
                Accept = ContentTypes.Html,
                ContentType = input.ContentType is null ? null : ContentTypes.Html
            };
            return FormatNegotiation.Formats( fallback ) is { Count: > 0 } formats ? formats[0] : null;
        }
    }

    /// <summary>
    /// <c>_set_vary_header</c>, which every <c>render</c> runs (not <c>head</c> or <c>redirect_to</c>):
    /// <c>Vary: Accept</c> when the format came from the <c>Accept</c> header, unless the response
    /// already varies.
    /// </summary>
    public FrontendResult SetVaryHeader( FrontendResult response )
    {
        ArgumentNullException.ThrowIfNull( response );

        if ( response.Header( HeaderNames.Vary ) is null && FormatNegotiation.ShouldApplyVaryHeader( NegotiationInput( ) ) )
        {
            response.Headers[HeaderNames.Vary] = HeaderNames.Accept;
        }

        return response;
    }

    private NegotiationInput NegotiationInput( )
    {
        string? format;
        try
        {
            format = RequestedFormat( );
        }
        catch ( BadRequestException )
        {
            // `params_readable?` rescues `BadRequest` and `ParseError`: the format param is ignored.
            format = null;
        }
        catch ( PayloadTooLargeException )
        {
            // The port's body bound (bounded request bodies) leaves the params unreadable too; without this, rendering its 413
            // would raise again while picking the error page's format.
            format = null;
        }

        return new NegotiationInput( format, Request.Header( HeaderNames.Accept ), Request.ContentType, Request.Path, Request.IsXhr );
    }

    /// <summary>
    /// The format the request names: the endpoint's forced <see cref="DefaultResponseFormat"/>, else
    /// <c>params[:format]</c>, else (when <c>Accept</c> is blank or <c>*/*</c>) the endpoint's default.
    /// </summary>
    private string? RequestedFormat( )
    {
        var configured = HttpContext.GetEndpoint( )?.Metadata.GetMetadata<DefaultResponseFormat>( );
        if ( configured is { Force: true } )
        {
            return configured.Symbol;
        }

        return Params[ParamNames.Format] switch
        {
            null => AcceptsAnyFormat( ) ? configured?.Symbol : null,
            { AsString: { } text } => text,
            var other => other.ToString( ),
        };
    }

    /// <summary>No <c>Accept</c> header, a blank one, or <c>*/*</c>.</summary>
    private bool AcceptsAnyFormat( )
    {
        var accept = Request.Header( HeaderNames.Accept );

        return string.IsNullOrWhiteSpace( accept ) || accept == "*/*";
    }
}
