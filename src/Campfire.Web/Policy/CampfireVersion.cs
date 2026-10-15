namespace Campfire.Web.Policy;

/// <summary>The running version: the app's and, when known, the Git revision it was built from.</summary>
public sealed record CampfireVersion( string AppVersion, string? GitRevision );
