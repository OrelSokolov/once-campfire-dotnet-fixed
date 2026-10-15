using Campfire.Web.Views.Models;
using RazorSlices;

namespace Campfire.Web.Views.Rendering;

/// <summary>
/// A page: rendered inside <c>layouts/application</c>, or inside turbo-rails' <c>layouts/turbo_rails/frame</c> for
/// Turbo-Frame requests (<see cref="ViewContext.Layout"/>). Pages set <see cref="PageTitle"/> and
/// <see cref="BodyClass"/>, and fill <c>content_for</c> regions (<see cref="ContentFor"/>) by overriding
/// <c>ExecuteSectionAsync</c>.
/// </summary>
/// <typeparam name="TModel">The page's model.</typeparam>
public abstract class PageSlice<TModel> : ViewSlice<TModel>, IUsesLayout<Views.layouts.application, LayoutModel>
    where TModel : IViewModel
{
    public LayoutModel LayoutModel => new( ViewContext, PageTitle, BodyClass );

    /// <summary><c>@page_title</c>.</summary>
    protected virtual string? PageTitle => null;

    /// <summary><c>@body_class</c>.</summary>
    protected virtual string? BodyClass => null;

    public RazorSlice<LayoutModel> CreateLayout( ) => Layouts.Create( ViewContext, LayoutModel );
}
