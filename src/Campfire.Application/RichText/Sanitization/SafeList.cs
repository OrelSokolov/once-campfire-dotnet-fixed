using System.Collections.Frozen;

namespace Campfire.Application.RichText.Sanitization;

/// <summary>A tag and attribute allowlist, as passed to <c>sanitize(html, tags:, attributes:)</c>.</summary>
internal sealed class SafeList
{
    /// <summary><c>Rails::HTML::Concern::Scrubber::SafeList::DEFAULT_ALLOWED_TAGS</c>.</summary>
    public static readonly string[] DefaultAllowedTags =
    [
        "a", "abbr", "acronym", "address", "b", "big", "blockquote", "br", "cite", "code", "dd", "del", "dfn", "div",
        "dl", "dt", "em", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img", "ins", "kbd", "li", "mark", "ol", "p",
        "pre", "samp", "small", "span", "strong", "sub", "sup", "time", "tt", "ul", "var",
    ];

    /// <summary>
    /// <c>Rails::HTML::Concern::Scrubber::SafeList::DEFAULT_ALLOWED_ATTRIBUTES</c> without <c>name</c>
    /// (security fix: "<c>name</c> DOM clobbering", decided fix). Rails keeps it, which lets a message
    /// clobber the page's DOM globals (<c>&lt;img name="body"&gt;</c> shadows <c>document.body</c>).
    /// Nothing Campfire's composer writes has one.
    /// </summary>
    public static readonly string[] DefaultAllowedAttributes =
        ["abbr", "alt", "cite", "class", "datetime", "height", "href", "lang", "src", "title", "width", "xml:lang"];

    /// <summary><c>ContentFilters::EDITOR_FORMATTING_TAGS</c> (app/helpers/content_filters.rb).</summary>
    public static readonly string[] EditorFormattingTags = ["s", "u", "mark", "table", "thead", "tbody", "tfoot", "tr", "th", "td"];

    /// <summary><c>ContentFilters::EDITOR_FORMATTING_ATTRIBUTES</c>.</summary>
    public static readonly string[] EditorFormattingAttributes = ["data-language"];

    /// <summary><c>ActionText::Attachment::ATTRIBUTES</c>, in order.</summary>
    public static readonly string[] AttachmentAttributes =
    [
        AttachmentAttributeNames.SignedGlobalId,
        AttachmentAttributeNames.ContentType,
        AttachmentAttributeNames.Url,
        AttachmentAttributeNames.Href,
        AttachmentAttributeNames.Filename,
        AttachmentAttributeNames.Filesize,
        AttachmentAttributeNames.Width,
        AttachmentAttributeNames.Height,
        AttachmentAttributeNames.Previewable,
        AttachmentAttributeNames.Presentation,
        AttachmentAttributeNames.Caption,
        AttachmentAttributeNames.Content,
    ];

    /// <summary><c>ContentFilters::SanitizeTags::ALLOWED_TAGS</c>.</summary>
    public static readonly string[] SanitizeTagsAllowedTags =
    [
        "a", "abbr", "acronym", "address", "b", "big", "blockquote", "br", "cite", "code", "dd", "del", "dfn", "div",
        "dl", "dt", "em", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "ins", "kbd", "li", "ol", "p", "pre", "samp",
        "small", "span", "strong", "sub", "sup", "time", "tt", "ul", "var",
        .. EditorFormattingTags,
        "action-text-attachment", "figure", "figcaption",
    ];

    private SafeList( IEnumerable<string> tags, IEnumerable<string> attributes )
    {
        Tags = tags.ToFrozenSet( StringComparer.Ordinal );
        Attributes = attributes.ToFrozenSet( StringComparer.Ordinal );
    }

    public FrozenSet<string> Tags { get; }

    public FrozenSet<string> Attributes { get; }

    /// <summary>Action View's <c>sanitize(html)</c> with no options: the sanitizer's class-level defaults.</summary>
    public static SafeList Defaults { get; } = new( DefaultAllowedTags, DefaultAllowedAttributes );

    /// <summary>
    /// <c>ActionText::ContentHelper.allowed_tags</c>/<c>allowed_attributes</c> as configured at boot:
    /// Action Text's defaults, then Lexxy's additions (lexxy/engine.rb, "lexxy.sanitization"), then
    /// Campfire's (lib/rails_ext/action_text_allowed_tags.rb).
    /// </summary>
    public static SafeList ActionText { get; } = new(
        [
            .. DefaultAllowedTags,
            "action-text-attachment", "figure", "figcaption", "video", "audio", "source", "embed", "table", "tbody", "tr", "th", "td",
            .. EditorFormattingTags,
        ],
        [
            .. DefaultAllowedAttributes,
            .. AttachmentAttributes,
            "controls", "poster", "data-language", "style", "value", "start",
            .. EditorFormattingAttributes,
        ] );

    /// <summary><c>ContentFilters::SanitizeAttributes</c>: SanitizeTags' tags, Action Text's attributes plus <c>class</c>.</summary>
    public static SafeList ContentFilter { get; } = new( SanitizeTagsAllowedTags, [.. ActionText.Attributes, "class"] );

    /// <summary><c>MessagesHelper::AUTO_LINK_ALLOWED_TAGS</c>/<c>AUTO_LINK_ALLOWED_ATTRIBUTES</c>.</summary>
    public static SafeList AutoLink { get; } = new(
        [.. DefaultAllowedTags, .. EditorFormattingTags],
        [.. DefaultAllowedAttributes, .. EditorFormattingAttributes] );

    public bool AllowsTag( string name ) => Tags.Contains( name );

    public bool AllowsAttribute( string name ) => Attributes.Contains( name );
}
