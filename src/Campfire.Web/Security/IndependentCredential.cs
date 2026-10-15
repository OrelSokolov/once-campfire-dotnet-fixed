namespace Campfire.Web.Security;

/// <summary>Only explicit non-cookie trust boundaries can opt out of browser forgery validation.</summary>
public sealed record IndependentCredential( string Reason );
