namespace Campfire.Application.RichText.Attachments;

/// <summary>What an <c>&lt;action-text-attachment&gt;</c> attaches.</summary>
internal abstract record Attachable
{
    public const string MentionContentType = "application/vnd.campfire.mention";

    public const string OpengraphEmbedContentType = "application/vnd.actiontext.opengraph-embed";

    /// <summary><c>attachable_content_type</c>, which only some attachables define (the others raise).</summary>
    public string AttachableContentType => this switch
    {
        UserAttachable => MentionContentType,
        OpengraphEmbedAttachable => OpengraphEmbedContentType,
        _ => throw RichTextException.NoMethod( "attachable_content_type" ),
    };
}

/// <summary>A mentioned user (<c>User::Mentionable</c>).</summary>
internal sealed record UserAttachable( MentionUser User ) : Attachable;

/// <summary><c>ActionText::Attachment::OpengraphEmbed</c> (lib/rails_ext/actiontext_opengraph_embeds.rb).</summary>
internal sealed record OpengraphEmbedAttachable( string? Href, string? Url, string? Filename, string? Description ) : Attachable
{
    private const string TwitterAvatarUrlPrefix = "https://pbs.twimg.com/profile_images";

    public bool IsTwitterAvatar => (Url ?? "").StartsWith( TwitterAvatarUrlPrefix, StringComparison.Ordinal );
}

/// <summary><c>ActionText::Attachables::ContentAttachment</c>.</summary>
internal sealed record ContentAttachable( string Content ) : Attachable;

/// <summary><c>ActionText::Attachables::RemoteImage</c>.</summary>
internal sealed record RemoteImageAttachable( string Url, string? Width, string? Height ) : Attachable;

/// <summary>Lexxy's <c>ActionText::Attachables::RemoteVideo</c>.</summary>
internal sealed record RemoteVideoAttachable( string Url, string ContentType, string? Width, string? Height, string? Filename ) : Attachable;

/// <summary><c>ActionText::Attachables::MissingAttachable</c>, remembering the model a still-valid SGID named.</summary>
internal sealed record MissingAttachable( string? SignedModel ) : Attachable;
