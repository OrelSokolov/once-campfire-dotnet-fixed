using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Rendering;

/// <summary>What the application layout needs from a page: <c>@page_title</c> and <c>@body_class</c>.</summary>
/// <param name="ViewContext">The request's view context.</param>
/// <param name="PageTitle"><c>@page_title</c>; the layout falls back to "Campfire".</param>
/// <param name="BodyClass"><c>@body_class</c>.</param>
public sealed record LayoutModel( ViewContext ViewContext, string? PageTitle = null, string? BodyClass = null ) : IViewModel;
