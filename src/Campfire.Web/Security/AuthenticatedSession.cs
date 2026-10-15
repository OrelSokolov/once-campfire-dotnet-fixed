using Campfire.Web.Policy;

namespace Campfire.Web.Security;

/// <summary>The request feature holding the browser session the cookie ticket was validated against.</summary>
public sealed record AuthenticatedSession( CurrentSession Session );
