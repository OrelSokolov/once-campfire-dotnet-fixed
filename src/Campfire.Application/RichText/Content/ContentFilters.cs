using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.Html;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Content;

/// <summary>
/// <c>ContentFilters::TextMessagePresentationFilters</c> (app/helpers/content_filters/*.rb):
/// RemoveSoloUnfurledLinkText, SanitizeTags, SanitizeAttributes, applied in that order.
/// </summary>
internal static class ContentFilters
{
    private static readonly string[] TwitterDomains = ["x.com", "twitter.com"];

    public static RichTextContent Apply( RichTextContent content, RichTextContext context )
    {
        var filtered = RemoveSoloUnfurledLinkText( content, context );
        filtered = SanitizeTags( filtered );

        return SanitizeAttributes( filtered );
    }

    /// <summary>A message that is nothing but a link to what it unfurls shows just the unfurl.</summary>
    public static RichTextContent RemoveSoloUnfurledLinkText( RichTextContent content, RichTextContext context )
    {
        ArgumentNullException.ThrowIfNull( content );
        ArgumentNullException.ThrowIfNull( context );

        var unfurledLinks = RichTextContent.AttachmentNodes( content.Root )
            .Where( node => node.GetAttribute( AttachmentAttributeNames.ContentType ) == Attachable.OpengraphEmbedContentType )
            .ToList( );
        var soloUnfurledUrl = unfurledLinks.Count == 1 ? OpengraphEmbeds.FromNode( unfurledLinks[0], context )?.Href : null;
        var plainText = content.ToPlainText( context );
        if ( NormalizeTweetUrl( soloUnfurledUrl ) != NormalizeTweetUrl( plainText ) )
        {
            return content;
        }

        var root = content.Root;
        var divs = root.DescendantsNamed( "div" );
        if ( divs.Count > 0 )
        {
            // A Trix body: every div gets the unfurl as its only content
            var unfurl = HtmlSerializer.ToHtml( unfurledLinks[0] );
            foreach ( var div in divs )
            {
                div.SetInnerHtml( unfurl );
            }
        }
        else
        {
            HtmlNode.RemoveAll( root.DescendantsNamed( "p" ).Where( paragraph => RichTextContent.AttachmentNodes( paragraph ).Count == 0 ) );
        }

        return content;
    }

    /// <summary>Removes every element outside the allowlist, together with its contents.</summary>
    public static RichTextContent SanitizeTags( RichTextContent content )
    {
        ArgumentNullException.ThrowIfNull( content );

        var allowed = SafeList.SanitizeTagsAllowedTags;
        HtmlNode.RemoveAll( content.Root.Descendants( ).Where( node => node.IsElement && !allowed.Contains( node.LocalName ) ) );

        return content;
    }

    /// <summary>Scrubs attributes with Rails' safe-list sanitizer over SanitizeTags' own tags.</summary>
    public static RichTextContent SanitizeAttributes( RichTextContent content )
    {
        ArgumentNullException.ThrowIfNull( content );

        return RichTextContent.Wrap( SafeListSanitizer.Sanitize( content.ToHtml( ), SafeList.ContentFilter ) );
    }

    /// <summary>x.com links and their query-less forms compare equal to the twitter.com URL an unfurl records.</summary>
    private static string? NormalizeTweetUrl( string? url )
    {
        if ( url is null )
        {
            return null;
        }

        var stripped = RubyStrings.Strip( url );
        if ( RubyStrings.IsBlank( url ) || !TwitterDomains.Any( domain => stripped.Contains( domain, StringComparison.Ordinal ) ) )
        {
            return url;
        }

        switch ( RubyUri.TryParse( url, out var parsed ) )
        {
            case RubyUriError.InvalidUri:
                return url;
            case RubyUriError.InvalidComponent:
                throw new RichTextException( RubyErrorClasses.InvalidUriComponent );
        }

        if ( parsed!.Host?.ToLowerInvariant( ) == "x.com" )
        {
            parsed.Host = "twitter.com";
        }

        parsed.Query = null;

        return parsed.ToString( );
    }
}
