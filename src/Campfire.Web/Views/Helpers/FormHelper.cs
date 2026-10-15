using System.Text;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>form_with</c>'s tags, <c>button_to</c>, <c>button_tag</c>, <c>hidden_field_tag</c> and <c>check_box_tag</c>,
/// reproducing ActionView's attribute order (<c>form_helper.rb</c>, <c>form_tag_helper.rb</c>, <c>tags/*.rb</c> and
/// <c>url_helper.rb#button_to</c>; Rust <c>helpers/forms.rs</c>). Block helpers split into an opening part the
/// template writes before the block's markup and a closing part it writes after.
/// </summary>
public static class FormHelper
{
    /// <summary><c>form_with(url:)</c>; chain <see cref="FormWith"/> options, then write <see cref="FormWith.Open"/>.</summary>
    public static FormWith FormWith( ViewContext viewContext, string url ) => new( viewContext, url );

    /// <summary><c>auto_submit_form_with(**attributes)</c> (<c>FormsHelper</c>): prepends the auto-submit controller.</summary>
    public static FormWith AutoSubmitFormWith( ViewContext viewContext, string url ) => new FormWith( viewContext, url ).AutoSubmit( );

    /// <summary>
    /// The hidden <c>authenticity_token</c> field of a form that posts, with an empty value for frontend compatibility.
    /// Omitted outside a request.
    /// </summary>
    public static SafeHtml TokenTag( ViewContext viewContext )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        if ( !viewContext.IncludeCsrfTags )
        {
            return SafeHtml.Empty;
        }

        return TagHelper.LegacyTag( "input", new TagAttributes( ).Type( "hidden" ).Name( "authenticity_token" ).Value( string.Empty ) );
    }

    /// <summary>The hidden <c>_method</c> field (<c>method_tag</c>).</summary>
    public static SafeHtml MethodTag( string method ) =>
        TagHelper.LegacyTag( "input", new TagAttributes( ).Type( "hidden" ).Name( "_method" ).Value( method ) );

    /// <summary><c>button_tag(options) { content }</c> / <c>form.button</c>: <c>{ name: "button", type: "submit" }</c> reverse-merged.</summary>
    public static SafeHtml ButtonTag( TagAttributes? options, SafeHtml content ) => TagHelper.ContentTag( "button", ButtonTagAttributes( options ), content );

    /// <summary>The opening tag of a block-form <c>button_tag</c> / <c>form.button</c>; the template writes <c>&lt;/button&gt;</c>.</summary>
    public static SafeHtml ButtonTagOpen( TagAttributes? options ) => TagHelper.OpenTag( "button", ButtonTagAttributes( options ) );

    /// <summary><c>hidden_field_tag(name, value, options)</c>.</summary>
    public static SafeHtml HiddenFieldTag( string name, string? value, TagAttributes? options = null )
    {
        var attributes = new TagAttributes( ).Type( "hidden" ).Name( name ).Id( SanitizeToId( name ) ).Attribute( "value", value );
        return TagHelper.LegacyTag( "input", attributes.Merge( options ) );
    }

    /// <summary><c>check_box_tag(name, value, checked, options)</c>.</summary>
    public static SafeHtml CheckBoxTag( string name, string value, bool isChecked, TagAttributes? options = null )
    {
        var attributes = new TagAttributes( ).Type( "checkbox" ).Name( name ).Id( SanitizeToId( name ) ).Value( value ).Merge( options );
        if ( isChecked )
        {
            attributes.Attribute( "checked", "checked" );
        }

        return TagHelper.LegacyTag( "input", attributes );
    }

    /// <summary><c>button_to(url, options) do ... end</c>. <paramref name="options"/> may carry <c>method</c>,
    /// <c>form_class</c> and the button's own attributes.</summary>
    public static ButtonTo ButtonTo( ViewContext viewContext, string url, TagAttributes? options = null ) =>
        new( viewContext, url, options ?? new TagAttributes( ) );

    /// <summary><c>sanitize_to_id</c>: <c>]</c> removed, other characters outside the id alphabet become "_".</summary>
    public static string SanitizeToId( string name )
    {
        ArgumentNullException.ThrowIfNull( name );

        var builder = new StringBuilder( name.Length );
        foreach ( var character in name.Replace( "]", string.Empty, StringComparison.Ordinal ) )
        {
            builder.Append( char.IsAsciiLetterOrDigit( character ) || character is '-' or '_' or ':' or '.' ? character : '_' );
        }

        return builder.ToString( );
    }

    /// <summary><c>object_name.gsub(/\]\[|[^-a-zA-Z0-9:.]/, "_").delete_suffix("_")</c>.</summary>
    public static string SanitizeObjectName( string name )
    {
        ArgumentNullException.ThrowIfNull( name );

        var builder = new StringBuilder( name.Length );
        foreach ( var character in name.Replace( "][", "_", StringComparison.Ordinal ) )
        {
            builder.Append( char.IsAsciiLetterOrDigit( character ) || character is '-' or ':' or '.' ? character : '_' );
        }

        var sanitized = builder.ToString( );
        return sanitized.EndsWith( '_' ) ? sanitized[..^1] : sanitized;
    }

    private static TagAttributes ButtonTagAttributes( TagAttributes? options ) => new TagAttributes( ).Name( "button" ).Type( "submit" ).Merge( options );
}
