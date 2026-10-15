namespace Campfire.Web.Policy;

/// <summary>Holds <see cref="AuthenticatedBy"/> in the request's <c>Current</c> state.</summary>
public sealed record AuthenticationMethod( AuthenticatedBy By );
