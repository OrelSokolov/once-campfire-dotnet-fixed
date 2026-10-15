using Campfire.Web.Security;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Http;

/// <summary>Materialize forms once, before routing, so browser POST overrides select the correct endpoint.</summary>
public sealed class FormAndMethodOverride( RequestDelegate next )
{
    public const string OverrideField = "_method";
    public const long MaximumBodyBytes = 32 * 1024 * 1024;
    public const int MaximumValueCount = 1024;
    public const int MaximumValueLength = 1024 * 1024;
    public const int MemoryThreshold = 64 * 1024;

    public async Task InvokeAsync( HttpContext httpContext )
    {
        var request = httpContext.Request;
        var limit = request.HasFormContentType ? MaximumBodyBytes : HttpOptions.ProductionMaxBufferedBodyBytes;
        if ( request.ContentLength > limit )
        {
            await ErrorResults.WriteAsync( httpContext, StatusCodes.Status413PayloadTooLarge );
            return;
        }

        if ( httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>( ) is { IsReadOnly: false } size )
        {
            size.MaxRequestBodySize = size.MaxRequestBodySize is { } current ? Math.Min( current, limit ) : limit;
        }

        if ( request.HasFormContentType && !BotRequestBody.HasRawBodyPath( request.Path ) && !HttpMethodChecks.IsGetOrHead( request.Method ) )
        {
            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync( httpContext.RequestAborted );
            }
            catch ( Exception error ) when ( error is (InvalidDataException or IOException) and not BadHttpRequestException )
            {
                await ErrorResults.WriteAsync( httpContext, StatusCodes.Status400BadRequest );
                return;
            }

            if ( HttpMethodChecks.IsPost( request.Method ) && form.TryGetValue( OverrideField, out var values ) )
            {
                var method = values.Count == 1 ? values[0]?.ToUpperInvariant( ) : null;
                if ( method is null || (method != HttpMethods.Delete && method != HttpMethods.Patch && method != HttpMethods.Put) )
                {
                    await ErrorResults.WriteAsync( httpContext, StatusCodes.Status400BadRequest );
                    return;
                }

                httpContext.Features.Set<IMethodOverrideFeature>( new MethodOverrideFeature( request.Method ) );
                request.Method = method;
            }
        }

        await next( httpContext );
    }

    public static void Configure( FormOptions options )
    {
        options.ValueCountLimit = MaximumValueCount;
        options.ValueLengthLimit = MaximumValueLength;
        options.MultipartBodyLengthLimit = MaximumBodyBytes;
        options.MemoryBufferThreshold = MemoryThreshold;
    }
}
