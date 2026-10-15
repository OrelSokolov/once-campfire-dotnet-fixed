namespace Campfire.Web.Assets;

/// <summary>
/// What <c>stylesheet_link_tag</c> renders, plus the preload links Rails adds to the response's
/// <c>link</c> header while rendering it (<c>config.action_view.preload_links_header</c>, on by default).
/// </summary>
public sealed record StylesheetTags( string Html, IReadOnlyList<string> PreloadLinks );
