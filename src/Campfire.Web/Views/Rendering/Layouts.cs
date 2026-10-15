using Campfire.Web.Views.Models;
using RazorSlices;

namespace Campfire.Web.Views.Rendering;

/// <summary>turbo-rails' <c>layout -&gt; { "turbo_rails/frame" if turbo_frame_request? }</c>.</summary>
internal static class Layouts
{
    public static RazorSlice<LayoutModel> Create( ViewContext viewContext, LayoutModel model )
    {
        if ( viewContext.Layout == LayoutKind.TurboFrame )
        {
            return Views.layouts.turbo_rails.frame.Create( model );
        }

        return Views.layouts.application.Create( model );
    }
}
