using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>button_to</c>: <c>&lt;form class="button_to" method="post" action="..."&gt;</c>, a <c>_method</c> field for
/// patch/put/delete, the button around the block's content, then the authenticity token.
/// </summary>
public sealed class ButtonTo
{
    internal ButtonTo( ViewContext viewContext, string url, TagAttributes options )
    {
        var method = options.Remove( "method" )?.Text ?? "post";
        var formClass = options.Remove( "form_class" )?.Text ?? "button_to";
        var formMethod = method == "get" ? "get" : "post";
        var methodField = method is "delete" or "patch" or "put" ? FormHelper.MethodTag( method ) : SafeHtml.Empty;
        var token = formMethod == "post" ? FormHelper.TokenTag( viewContext ) : SafeHtml.Empty;

        options.Attribute( "type", "submit" );
        var form = new TagAttributes( ).Class( formClass ).Method( formMethod ).Attribute( "action", url );
        Open = SafeHtml.Concat( TagHelper.OpenTag( "form", form ), methodField, TagHelper.OpenTag( "button", options ) );
        Close = SafeHtml.Raw( "</button>" + token.Html + "</form>" );
    }

    /// <summary>The form tag, the method field and the opening button tag.</summary>
    public SafeHtml Open { get; }

    /// <summary>The closing button tag, the authenticity token and the closing form tag.</summary>
    public SafeHtml Close { get; }

    /// <summary>The whole button around already-rendered <paramref name="content"/>.</summary>
    public SafeHtml Wrap( SafeHtml content ) => SafeHtml.Concat( Open, content, Close );
}
