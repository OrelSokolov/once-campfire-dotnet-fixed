using System.Text.RegularExpressions;
using Campfire.Application.RichText.Html;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Attachments;

/// <summary>
/// <c>ActionText::Attachment::OpengraphEmbed.from_node</c> (lib/rails_ext/actiontext_opengraph_embeds.rb):
/// a link preview's details, with its link and image kept only when they are absolute http(s) URLs
/// on a named host other than this Campfire's.
/// </summary>
internal static partial class OpengraphEmbeds
{
    private static readonly char[] ClassSeparators = [' ', '\t', '\n', '\r'];

    /// <summary>The embed a node describes, or <c>null</c> when its content type isn't an opengraph embed's.</summary>
    public static OpengraphEmbedAttachable? FromNode( HtmlNode node, RichTextContext context )
    {
        ArgumentNullException.ThrowIfNull( node );
        ArgumentNullException.ThrowIfNull( context );

        // `node["content-type"].match(OPENGRAPH_EMBED_CONTENT_TYPE)`: a String pattern becomes a Regexp
        if ( node.GetAttribute( AttachmentAttributeNames.ContentType ) is not { } contentType || !OpengraphContentType( ).IsMatch( contentType ) )
        {
            return null;
        }

        var host = context.RequestHost ?? "";
        if ( RubyStrings.Presence( node.GetAttribute( AttachmentAttributeNames.Filename ) ) is not null )
        {
            return new OpengraphEmbedAttachable(
                WebUrl( node.GetAttribute( AttachmentAttributeNames.Href ), host ),
                WebUrl( node.GetAttribute( AttachmentAttributeNames.Url ), host ),
                node.GetAttribute( AttachmentAttributeNames.Filename ),
                node.GetAttribute( AttachmentAttributeNames.Caption ) );
        }

        return FromContent( node.GetAttribute( AttachmentAttributeNames.Content ) ?? "", host );
    }

    /// <summary><c>web_url</c>: an absolute http(s) URL on a named host other than this Campfire's.</summary>
    public static string? WebUrl( string? value, string requestHost )
    {
        ArgumentNullException.ThrowIfNull( requestHost );

        if ( RubyStrings.IsBlank( value ) )
        {
            return null;
        }

        switch ( RubyUri.TryParse( value!, out var parsed ) )
        {
            case RubyUriError.InvalidUri:
                return null;
            case RubyUriError.InvalidComponent:
                throw new RichTextException( RubyErrorClasses.InvalidUriComponent );
        }

        return parsed!.IsHttp && IsElsewhere( parsed.Host, requestHost ) ? value : null;
    }

    /// <summary>
    /// <c>attributes_from_content</c>: the details Lexxy serializes as the embed's content markup. Rails
    /// parses it with <c>Nokogiri::HTML</c> (libxml2's HTML4 parser); the HTML5 parser agrees with it on
    /// the markup the embed partial and Lexxy produce.
    /// </summary>
    private static OpengraphEmbedAttachable FromContent( string content, string host )
    {
        var fragment = HtmlFragmentParser.ParseFragment( content );
        var all = fragment.Descendants( );
        var title = all.FirstOrDefault( node => HasClass( node, "og-embed__title" ) );
        var link = title?.Descendants( ).FirstOrDefault( node => node.IsNamed( "a" ) );
        var image = all.FirstOrDefault( node => node.IsNamed( "img" ) && node.Ancestors( ).Any( ancestor => HasClass( ancestor, "og-embed__image" ) ) );
        var description = all.FirstOrDefault( node => HasClass( node, "og-embed__description" ) );

        return new OpengraphEmbedAttachable(
            WebUrl( link?.GetAttribute( "href" ), host ),
            WebUrl( image?.GetAttribute( "src" ), host ),
            (link ?? title) is { } named ? RubyStrings.Strip( named.TextContent( ) ) : null,
            description is null ? null : RubyStrings.Strip( description.TextContent( ) ) );
    }

    /// <summary>A CSS class match as Nokogiri's XPath does it: tokens separated by XML whitespace.</summary>
    private static bool HasClass( HtmlNode node, string className ) =>
        node.GetAttribute( "class" ) is { } classes && classes.Split( ClassSeparators ).Contains( className );

    private static bool IsElsewhere( string? host, string requestHost )
    {
        if ( host is null || !IsNamedHost( host ) )
        {
            return false;
        }

        return CanonicalHost( host ) != CanonicalHost( requestHost );
    }

    /// <summary>
    /// <c>named_host?</c>: present, unescaped, dotted, and ending in a label with a letter that isn't
    /// hexadecimal (<c>host.split(".").last</c>, where Ruby drops trailing empty labels and
    /// <c>nil.match?</c> raises).
    /// </summary>
    private static bool IsNamedHost( string host )
    {
        if ( RubyStrings.IsBlank( host ) || host.Contains( '%', StringComparison.Ordinal ) || !host.Contains( '.', StringComparison.Ordinal ) )
        {
            return false;
        }

        var trimmed = host.TrimEnd( '.' );
        if ( trimmed.Length == 0 )
        {
            throw RichTextException.NoMethod( "match?" );
        }

        var label = trimmed[(trimmed.LastIndexOf( '.' ) + 1)..];

        return label.Any( char.IsAsciiLetter ) && !label.StartsWith( "0x", StringComparison.OrdinalIgnoreCase );
    }

    private static string CanonicalHost( string host )
    {
        var lower = host.ToLowerInvariant( );

        return lower.EndsWith( '.' ) ? lower[..^1] : lower;
    }

    [GeneratedRegex( Attachable.OpengraphEmbedContentType, RegexOptions.CultureInvariant )]
    private static partial Regex OpengraphContentType( );
}
