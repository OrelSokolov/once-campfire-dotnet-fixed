namespace Campfire.Web.Http;

/// <summary><c>ActionController::BadRequest</c>: the request's parameters can't be read (400).</summary>
public sealed class BadRequestException( string message, Exception? innerException = null )
    : HttpFailureException( StatusCodes.Status400BadRequest, message, innerException );
