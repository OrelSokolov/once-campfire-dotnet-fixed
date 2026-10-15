using System.Text;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>ActionView::Helpers::TagHelper</c> element builders (Rust <c>helpers/tag.rs</c>): <c>content_tag</c>, the
/// <c>tag.*</c> builder and the legacy <c>tag(name, options)</c>.
/// </summary>
public static class TagHelper
{
    /// <summary>HTML void elements, which the <c>tag.*</c> builder renders without a closing tag.</summary>
    private static readonly HashSet<string> VoidElements = new( StringComparer.Ordinal )
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "keygen", "link", "meta", "source", "track", "wbr",
    };

    /// <summary>A new, empty options hash.</summary>
    public static TagAttributes Attributes( ) => new( );

    /// <summary><c>raw</c> / <c>String#html_safe</c>.</summary>
    public static SafeHtml Raw( string? html ) => SafeHtml.Raw( html );

    /// <summary><c>h(text)</c> as an html_safe value.</summary>
    public static SafeHtml Text( string? text ) => SafeHtml.Text( text );

    /// <summary><c>content_tag(name, content, options)</c> with already-safe content.</summary>
    public static SafeHtml ContentTag( string name, TagAttributes? attributes, SafeHtml content )
    {
        ArgumentNullException.ThrowIfNull( content );

        return Build( name, attributes, content.Html, textareaNewline: name == "textarea" );
    }

    /// <summary><c>content_tag</c> with plain-text content, escaped.</summary>
    public static SafeHtml ContentTagText( string name, TagAttributes? attributes, string? content ) =>
        ContentTag( name, attributes, SafeHtml.Text( content ) );

    /// <summary>The opening half of a block-form <c>content_tag</c> / <c>tag.name do</c>.</summary>
    public static SafeHtml OpenTag( string name, TagAttributes? attributes )
    {
        return Build( TagAttributes.Dasherize( name ), attributes, null );
    }

    /// <summary>
    /// <c>tag.name(**options)</c> without content: void elements get no closing tag and no slash, others render
    /// empty. Underscores in the name become dashes (<c>tag.turbo_frame</c>).
    /// </summary>
    public static SafeHtml BuilderTag( string name, TagAttributes? attributes )
    {
        var element = TagAttributes.Dasherize( name );
        return Build( element, attributes, VoidElements.Contains( element ) ? null : string.Empty );
    }

    /// <summary>Legacy <c>tag(:name, options)</c>, used by <c>image_tag</c>, form fields and <c>csrf_meta_tags</c>: <c>" /&gt;"</c>.</summary>
    public static SafeHtml LegacyTag( string name, TagAttributes? attributes )
    {
        return Build( name, attributes, null, legacy: true );
    }

    private static SafeHtml Build( string name, TagAttributes? attributes, string? content, bool legacy = false, bool textareaNewline = false )
    {
        var newline = content is not null && textareaNewline;
        var length = 1 + name.Length + (attributes?.RenderedLength ?? 0) + (legacy ? 3 : 1);
        if ( content is not null )
        {
            length += content.Length + name.Length + 3 + (newline ? 1 : 0);
        }

        return SafeHtml.Raw( string.Create( length, (name, attributes, content, legacy, newline), static ( output, state ) =>
        {
            output[0] = '<';
            state.name.CopyTo( output[1..] );
            var position = 1 + state.name.Length;
            position += state.attributes?.RenderTo( output[position..] ) ?? 0;
            var ending = state.legacy ? " />" : ">";
            ending.CopyTo( output[position..] );
            position += ending.Length;
            if ( state.content is { } content )
            {
                if ( state.newline )
                {
                    output[position++] = '\n';
                }

                content.CopyTo( output[position..] );
                position += content.Length;
                "</".CopyTo( output[position..] );
                position += 2;
                state.name.CopyTo( output[position..] );
                position += state.name.Length;
                output[position++] = '>';
            }

            if ( position != output.Length )
            {
                throw new InvalidOperationException( "Tag sizing and rendering disagree." );
            }
        } ) );
    }

    /// <summary><c>token_list</c> / <c>class_names</c>: the names whose condition holds, joined by spaces.</summary>
    public static string TokenList( params ReadOnlySpan<(string Name, bool Include)> tokens )
    {
        var builder = new StringBuilder( );
        foreach ( var (name, include) in tokens )
        {
            if ( !include )
            {
                continue;
            }

            if ( builder.Length > 0 )
            {
                builder.Append( ' ' );
            }

            builder.Append( name );
        }

        return builder.ToString( );
    }
}
