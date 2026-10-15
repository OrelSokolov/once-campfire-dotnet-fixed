namespace Campfire.Web.Http;

/// <summary><c>ActionController::Redirecting::UnsafeRedirectError</c>: a redirect to another host or a malformed URL (400).</summary>
public sealed class UnsafeRedirectException( string message ) : HttpFailureException( StatusCodes.Status400BadRequest, message );
