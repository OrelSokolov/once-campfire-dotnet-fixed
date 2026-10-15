namespace Campfire.Web.Http;

/// <summary>The <see cref="IMethodOverrideFeature"/> <see cref="FormAndMethodOverride"/> sets.</summary>
internal sealed record MethodOverrideFeature( string OriginalMethod ) : IMethodOverrideFeature;
