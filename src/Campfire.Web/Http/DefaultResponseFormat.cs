namespace Campfire.Web.Http;

/// <summary>Fallback for unrestricted Accept, or an explicit representation on a mapped suffix route.</summary>
public sealed record DefaultResponseFormat( string Symbol, bool Force = false );
