namespace Campfire.Web.Tests.Views.Support;

/// <summary>The request facts of a page-golden case that aren't in <c>facts.json</c>: a partial render and the flash.</summary>
internal sealed record PageGoldenRequest( bool Partial = false, string? FlashNotice = null, string? FlashAlert = null );
