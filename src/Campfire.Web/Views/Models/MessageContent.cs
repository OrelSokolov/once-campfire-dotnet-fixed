namespace Campfire.Web.Views.Models;

/// <summary><c>Message#content_type</c> with what each presentation needs.</summary>
public abstract record MessageContent
{
    private MessageContent( )
    {
    }

    /// <summary>The presentation filters' output after <c>auto_link</c>, from the rich-text component.</summary>
    public sealed record Text( SafeHtml Html ) : MessageContent;

    /// <summary>A <c>/play &lt;name&gt;</c> message's sound.</summary>
    /// <param name="Url"><c>asset_path(sound.asset_path)</c>.</param>
    /// <param name="Image">The sound's image, when it has one.</param>
    /// <param name="Caption">The sound's text, shown without an image.</param>
    public sealed record Sound( string Url, SoundImageView? Image, string? Caption ) : MessageContent;

    /// <summary>The message's Active Storage attachment.</summary>
    public sealed record Attachment( AttachmentView View ) : MessageContent;

    /// <summary>Rendering raised: <c>message_tag</c> rescues and renders <c>messages/_unrenderable</c>.</summary>
    public sealed record Unrenderable : MessageContent;
}
