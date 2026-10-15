using Campfire.Application.RichText;
using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.Content;

namespace Campfire.Application.Tests.RichText.Support;

/// <summary>
/// The fixtures of the reference app's rich text tests: david (<c>users(:david)</c>) and the pets room,
/// with the SGIDs Rails mints for them under the test secret.
/// </summary>
internal static class ReferenceFixtures
{
    public const string DavidSignedGlobalId = "eyJfcmFpbHMiOnsiZGF0YSI6ImdpZDovL2NhbXBmaXJlL1VzZXIvMT9leHBpcmVzX2luIiwicHVyIjoiYXR0YWNoYWJsZSJ9fQ==" +
        "--f7d8e8773314d3310320f3cdd08e5597bb51ca1a";

    /// <summary>A Room's attachable SGID message, as <c>rooms(:pets).to_sgid(expires_in: nil, for: "attachable")</c> mints it.</summary>
    public const string RoomSignedGlobalIdMessage = "eyJfcmFpbHMiOnsiZGF0YSI6ImdpZDovL2NhbXBmaXJlL1Jvb20vMT9leHBpcmVzX2luIiwicHVyIjoiYXR0YWNoYWJsZSJ9fQ==";

    public const string BasecampUnfurl =
        "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://basecamp.com/assets/general/opengraph.png\" " +
            "href=\"https://basecamp.com/\" filename=\"Project management software, online collaboration\" caption=\"Trusted by millions.\">" +
            "</action-text-attachment>";

    public const string TwitterUnfurl =
        "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" " +
            "url=\"https://pbs.twimg.com/ext_tw_video_thumb/1752476502791503873/pu/img/WEAqUgarUxWjPNHD.jpg\" " +
            "href=\"https://twitter.com/dhh/status/1752476663303323939\" filename=\"DHH (@dhh)\" caption=\"We're playing.\"></action-text-attachment>";

    public static MentionUser David { get; } = new( 1, "David", "David – Founder", DavidSignedGlobalId, "/users/1", "/users/1/avatar?v=1" );

    public static RichTextContext Context { get; } = new( new FixtureResolver( ), "once.campfire.test" );

    /// <summary>test_helper.rb's <c>mention_attachment_for(:david)</c>.</summary>
    public static string MentionAttachmentForDavid( ) =>
        $"<action-text-attachment sgid=\"{DavidSignedGlobalId}\" content-type=\"application/vnd.campfire.mention\" " +
            $"content=\"{AttachablePartials.RenderMention( David ).Replace( "\"", "&quot;", StringComparison.Ordinal )}\"></action-text-attachment>";

    /// <summary><c>TextMessagePresentationFilters.apply(body).to_html</c>.</summary>
    public static string Filtered( string body ) => ContentFilters.Apply( RichTextContent.Load( body, Context ), Context ).ToHtml( );

    /// <summary><c>ActionText::Content.new(body).to_html</c>.</summary>
    public static string Loaded( string body ) => RichTextContent.Load( body, Context ).ToHtml( );

    public static string Presentation( string body ) => RichTextPipeline.RenderMessage( body, Context );

    public static string PlainText( string body ) => RichTextPipeline.ToPlainText( body, Context );

    private sealed class FixtureResolver : IAttachableResolver
    {
        public SignedAttachableLookup LocateSigned( string signedGlobalId ) =>
            signedGlobalId == DavidSignedGlobalId ? SignedAttachableLookup.ForUser( David ) : SignedAttachableLookup.Invalid;

    }
}
