namespace Campfire.Web.Http;

/// <summary><c>ActionController::UnknownFormat</c>: no format <c>respond_to</c> offers is acceptable (406).</summary>
public sealed class UnknownFormatException( string message ) : HttpFailureException( StatusCodes.Status406NotAcceptable, message );
