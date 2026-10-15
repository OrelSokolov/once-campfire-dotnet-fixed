namespace Campfire.Web.Security;

/// <summary>What a cable connection keeps from the browser ticket: the session row, its token and the ticket's expiry.</summary>
internal readonly record struct CableCredential( long SessionId, string Token, DateTimeOffset? ExpiresUtc = null );
