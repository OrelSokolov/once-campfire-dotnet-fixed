using Campfire.Application.Data;
using Campfire.Application.Storage;
using Campfire.Web.Http;
using Campfire.Web.Views;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;

namespace Campfire.Web.Security;

/// <summary>Write errors once without routing the original unsafe request through the pipeline again.</summary>
public static partial class ErrorResults
{
    public static async Task HandleExceptionAsync( HttpContext httpContext )
    {
        var error = httpContext.Features.Get<IExceptionHandlerFeature>( )?.Error;
        if ( error is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested )
        {
            return;
        }

        var status = StatusFor( error );
        if ( status == StatusCodes.Status500InternalServerError )
        {
            LogRequestFailed( httpContext.RequestServices.GetRequiredService<RequestContextServices>( ).Logger, error );
        }

        await WriteAsync( httpContext, status );
    }

    public static Task WriteStatusPageAsync( StatusCodeContext context ) => WriteAsync( context.HttpContext, context.HttpContext.Response.StatusCode );

    public static async Task WriteAsync( HttpContext httpContext, int status )
    {
        httpContext.Response.StatusCode = status;
        var title = ReasonPhrases.GetReasonPhrase( status );
        if ( httpContext.Request.Headers.Accept.ToString( ).Contains( ContentTypes.Json, StringComparison.OrdinalIgnoreCase ) )
        {
            await TypedResults.Problem( statusCode: status, title: title ).ExecuteAsync( httpContext );
            return;
        }

        var publicPath = httpContext.RequestServices.GetRequiredService<RequestContextServices>( ).Options.PublicPath;
        string? styledPage = null;
        if ( publicPath is not null && status is StatusCodes.Status404NotFound or StatusCodes.Status500InternalServerError )
        {
            styledPage = Path.Combine( publicPath, $"{status}.html" );
        }

        string html;
        if ( styledPage is not null && File.Exists( styledPage ) )
        {
            html = await File.ReadAllTextAsync( styledPage, httpContext.RequestAborted );
        }
        else
        {
            var encodedTitle = Escaping.Escape( title );
            html = $"<!doctype html><html><head><title>{status} {encodedTitle}</title></head><body><h1>{encodedTitle}</h1></body></html>";
        }

        await Results.Content( html, ContentTypes.HtmlUtf8, statusCode: status ).ExecuteAsync( httpContext );
    }

    /// <summary>The status <c>ActionDispatch::ExceptionWrapper</c>'s rescue responses give an exception; 500 for anything else.</summary>
    private static int StatusFor( Exception? error ) => error switch
    {
        BadHttpRequestException request => request.StatusCode,
        InvalidDataException => StatusCodes.Status400BadRequest,
        HttpFailureException failure => failure.Status,
        InvalidMimeTypeException => StatusCodes.Status406NotAcceptable,
        RecordNotFoundException => StatusCodes.Status404NotFound,
        RecordInvalidException => StatusCodes.Status422UnprocessableEntity,
        StorageException { Failure: StorageFailure.InvalidSignature or StorageFailure.FileNotFound } => StatusCodes.Status404NotFound,
        StorageException { Failure: StorageFailure.LimitExceeded } => StatusCodes.Status413PayloadTooLarge,
        StorageException { Failure: StorageFailure.InvalidVariation or StorageFailure.Integrity } => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError
    };

    [LoggerMessage( Level = LogLevel.Error, Message = "Request failed" )]
    private static partial void LogRequestFailed( ILogger logger, Exception? exception );
}
