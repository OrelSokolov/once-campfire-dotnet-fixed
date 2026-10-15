namespace Campfire.Web.Http;

/// <summary>A request body over the port's bound (413).</summary>
public sealed class PayloadTooLargeException( string message ) : HttpFailureException( StatusCodes.Status413PayloadTooLarge, message );
