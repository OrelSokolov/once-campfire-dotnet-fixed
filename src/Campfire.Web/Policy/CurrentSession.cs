namespace Campfire.Web.Policy;

/// <summary><c>Current.session</c>: the <c>sessions</c> row (token, holder) and its user.</summary>
public sealed record CurrentSession( long Id, string Token, CurrentUser User, object? Record = null );
