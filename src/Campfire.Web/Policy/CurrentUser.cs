namespace Campfire.Web.Policy;

/// <summary><c>Current.user</c>: the id policies need, and the application's user record.</summary>
public sealed record CurrentUser( long Id, object? Record = null );
