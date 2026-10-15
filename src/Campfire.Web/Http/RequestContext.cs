using Campfire.Web.Http.Inputs;
using Campfire.Web.Policy;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Http;

/// <summary>Request-local rendering and operation inputs. Routing and security belong to ASP.NET Core.</summary>
public sealed partial class RequestContext
{
    /// <summary>The most a bot may post as a raw message body.</summary>
    private const int MaximumBotBodyBytes = 1024 * 1024;

    private const int BotBodyChunkBytes = 8192;

    private readonly RequestContextServices _services;
    private readonly Dictionary<Type, object> _current = [];
    private readonly FrontendFields _fields;
    private Flash? _flash;

    private RequestContext( HttpContext httpContext, RequestContextServices services, FrontendFields fields, ReadOnlyMemory<byte> rawPost )
    {
        HttpContext = httpContext;
        _services = services;
        _fields = fields;
        RawPost = rawPost;
        Request = new FrontendRequest( httpContext );
        Cookies = new RequestCookies( httpContext );
        if ( httpContext.Features.Get<AuthenticatedSession>( ) is { Session: var session } )
        {
            SetCurrent( session );
            SetCurrent( session.User );
            SetCurrent( new AuthenticationMethod( AuthenticatedBy.Session ) );
        }

        if ( httpContext.Features.Get<AuthenticatedBot>( ) is { User: var bot } )
        {
            SetCurrent( bot );
            SetCurrent( new AuthenticationMethod( AuthenticatedBy.BotKey ) );
        }
    }

    public HttpContext HttpContext { get; }

    /// <summary>Cancelled when the client disconnects.</summary>
    public CancellationToken RequestAborted => HttpContext.RequestAborted;

    public FrontendRequest Request { get; }
    public RequestCookies Cookies { get; }
    public HttpOptions Options => _services.Options;
    public TimeProvider Clock => _services.Clock;
    public DateTimeOffset Now => Clock.GetUtcNow( );
    public IHeaderDictionary ResponseHeaders { get; } = new HeaderDictionary( );
    public CacheControl CacheControl { get; } = new( );
    public ReadOnlyMemory<byte> RawPost { get; }
    public InputObject Params => _fields.Combined;
    public InputObject RequestParameters => _fields.Form;
    public InputObject QueryParameters => _fields.Query;
    public Flash Flash => _flash ??= FlashCookie.Read( HttpContext );
    internal RequestContextServices Services => _services;

    public static async Task<RequestContext> CreateAsync( HttpContext httpContext, CancellationToken cancellationToken, bool bodyConsumed = false )
    {
        if ( httpContext.Features.Get<RequestContext>( ) is { } existing )
        {
            return existing;
        }

        var fields = await FrontendInputs.ReadAsync( httpContext );
        var rawPost = ReadOnlyMemory<byte>.Empty;
        if ( BotRequestBody.HasRawBody( httpContext ) )
        {
            rawPost = await ReadBotBodyAsync( httpContext.Request.Body, cancellationToken );
        }
        else if ( !bodyConsumed && HasNonFormBody( httpContext ) )
        {
            throw new BadHttpRequestException( "Expected a form body.", StatusCodes.Status415UnsupportedMediaType );
        }

        var requestContext = new RequestContext( httpContext, httpContext.RequestServices.GetRequiredService<RequestContextServices>( ), fields, rawPost );
        httpContext.Features.Set( requestContext );
        return requestContext;
    }

    public static RequestContext From( HttpContext httpContext ) => httpContext.Features.Get<RequestContext>( )
        ?? throw new InvalidOperationException( "The endpoint has not initialized its request context." );
    public static RequestContext? Find( HttpContext httpContext ) => httpContext.Features.Get<RequestContext>( );
    public string? Param( string name ) => _fields.Combined.GetString( name );
    public void SetCurrent<T>( T value ) where T : class => _current[typeof( T )] = value;
    public T? Current<T>( ) where T : class => _current.TryGetValue( typeof( T ), out var value ) ? ( T ) value : null;

    internal void CommitFlash( )
    {
        if ( _flash is not null )
        {
            FlashCookie.Write( HttpContext, _flash );
        }
    }

    /// <summary>A bot's message body as sent, up to <see cref="MaximumBotBodyBytes"/> (413 beyond it).</summary>
    private static async Task<ReadOnlyMemory<byte>> ReadBotBodyAsync( Stream body, CancellationToken cancellationToken )
    {
        using var buffer = new MemoryStream( );
        var chunk = new byte[BotBodyChunkBytes];
        int read;
        while ( (read = await body.ReadAsync( chunk, cancellationToken )) != 0 )
        {
            if ( buffer.Length + read > MaximumBotBodyBytes )
            {
                throw new BadHttpRequestException( "Bot body exceeds the limit.", StatusCodes.Status413PayloadTooLarge );
            }

            buffer.Write( chunk, 0, read );
        }

        return buffer.ToArray( );
    }

    /// <summary>
    /// A request carrying a body the form reader can't see (not GET, HEAD or OPTIONS, not form content): endpoints
    /// read their parameters from forms, so it is a 415 rather than silently empty parameters.
    /// </summary>
    private static bool HasNonFormBody( HttpContext httpContext ) =>
        !HttpMethodChecks.IsGetOrHead( httpContext.Request.Method )
        && !HttpMethodChecks.IsOptions( httpContext.Request.Method )
        && !httpContext.Request.HasFormContentType
        && httpContext.Features.Get<IHttpRequestBodyDetectionFeature>( )?.CanHaveBody == true;
}
