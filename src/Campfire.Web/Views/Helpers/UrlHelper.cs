using Campfire.Web.Paths;

namespace Campfire.Web.Views.Helpers;

/// <summary><c>link_to</c>, <c>link_to_if</c> and <c>mail_to</c> (<c>UrlHelper</c>; Rust <c>helpers/links.rs</c> and <c>url.rs</c>).</summary>
public static class UrlHelper
{
    /// <summary><c>link_to(url, options) { content }</c>: <c>href</c> goes after the given options.</summary>
    public static SafeHtml LinkTo( string url, TagAttributes? options, SafeHtml content ) =>
        TagHelper.ContentTag( "a", (options ?? new TagAttributes( )).Attribute( "href", url ), content );

    /// <summary>The opening <c>&lt;a&gt;</c> of a block-form <c>link_to(url, options) do ... end</c>; the template closes it.</summary>
    public static SafeHtml LinkToOpen( string url, TagAttributes? options = null ) =>
        TagHelper.OpenTag( "a", (options ?? new TagAttributes( )).Attribute( "href", url ) );

    /// <summary><c>link_to(text, url, options)</c> with a plain-text name.</summary>
    public static SafeHtml LinkToText( string text, string url, TagAttributes? options = null ) => LinkTo( url, options, SafeHtml.Text( text ) );

    /// <summary><c>link_to_if(condition, name, url, options)</c>: just the escaped name when false.</summary>
    public static SafeHtml LinkToIf( bool condition, string text, string? url, TagAttributes? options = null ) =>
        condition ? LinkToText( text, url ?? string.Empty, options ) : SafeHtml.Text( text );

    /// <summary>
    /// <c>mail_to(email)</c>: the address percent-escaped (<c>ERB::Util.url_encode</c>, keeping "@") in the href and the
    /// plain address as the link text.
    /// </summary>
    public static SafeHtml MailTo( string? email )
    {
        var address = email ?? string.Empty;
        var encoded = UrlEncoding.UrlEncode( address ).Replace( "%40", "@", StringComparison.Ordinal );
        return TagHelper.ContentTag( "a", new TagAttributes( ).Attribute( "href", "mailto:" + encoded ), SafeHtml.Text( address ) );
    }

}
