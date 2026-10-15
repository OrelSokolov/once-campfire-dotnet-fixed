using Campfire.Web.Policy;

namespace Campfire.Web.Security;

/// <summary>The request feature holding the bot a valid bot key authenticated.</summary>
public sealed record AuthenticatedBot( CurrentUser User );
