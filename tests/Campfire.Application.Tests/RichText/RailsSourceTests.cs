using System.Text.RegularExpressions;
using Campfire.Application.RichText;
using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Tests.RichText.Support;

namespace Campfire.Application.Tests.RichText;

/// <summary>The allowlists and markup the pipeline hard-codes, checked against the Rails source they come from.</summary>
public sealed class RailsSourceTests
{
    [Fact]
    public void Editor_formatting_allowlists_match_content_filters_rb( )
    {
        var source = RepositoryPaths.ReadReferenceText( "app/helpers/content_filters.rb" );

        Assert.Equal( WordList( source, "EDITOR_FORMATTING_TAGS" ), SafeList.EditorFormattingTags );
        Assert.Equal( WordList( source, "EDITOR_FORMATTING_ATTRIBUTES" ), SafeList.EditorFormattingAttributes );
    }

    [Fact]
    public void Sanitize_tags_allowlist_matches_sanitize_tags_rb( )
    {
        var source = RepositoryPaths.ReadReferenceText( "app/helpers/content_filters/sanitize_tags.rb" );
        Assert.Contains(
            "+ ContentFilters::EDITOR_FORMATTING_TAGS +\n    [ ActionText::Attachment.tag_name, \"figure\", \"figcaption\" ]",
            source,
            StringComparison.Ordinal );

        string[] expected = [.. WordList( source, "ALLOWED_TAGS" ), .. SafeList.EditorFormattingTags, "action-text-attachment", "figure", "figcaption"];
        Assert.Equal( expected, SafeList.SanitizeTagsAllowedTags );
    }

    [Fact]
    public void Tweet_domains_match_remove_solo_unfurled_link_text_rb( ) =>
        Assert.Equal(
            ["x.com", "twitter.com"],
            WordList( RepositoryPaths.ReadReferenceText( "app/helpers/content_filters/remove_solo_unfurled_link_text.rb" ), "TWITTER_DOMAINS" ) );

    [Fact]
    public void Content_layout_matches_the_action_text_layout( )
    {
        var layout = RepositoryPaths.ReadReferenceText( "app/views/layouts/action_text/contents/_content.html.erb" );

        Assert.Equal( "<div class=\"lexxy-content\">\n  <%= yield -%>\n</div>\n", layout );
        Assert.Equal( "<div class=\"lexxy-content\">\n  x\n</div>\n", RichTextPipeline.RenderHtml( "x", ReferenceFixtures.Context ) );
    }

    [Fact]
    public void Mention_partial_matches_users_mention_html_erb( )
    {
        var partial = RepositoryPaths.ReadReferenceText( "app/views/users/_mention.html.erb" );
        Assert.EndsWith(
            "<span class=\"mention\" sgid=\"<%= user.attachable_sgid %>\"><%= avatar_tag user %> <%= user.name %></span>\n",
            partial,
            StringComparison.Ordinal );

        var rendered = AttachablePartials.RenderMention( ReferenceFixtures.David );
        Assert.StartsWith( $"<span class=\"mention\" sgid=\"{ReferenceFixtures.DavidSignedGlobalId}\"><a title=", rendered, StringComparison.Ordinal );
        Assert.EndsWith( "</a> David</span>\n", rendered, StringComparison.Ordinal );
    }

    [Fact]
    public void Opengraph_constants_match_actiontext_opengraph_embeds_rb( )
    {
        var source = RepositoryPaths.ReadReferenceText( "lib/rails_ext/actiontext_opengraph_embeds.rb" );

        Assert.Contains( $"OPENGRAPH_EMBED_CONTENT_TYPE = \"{Attachable.OpengraphEmbedContentType}\"", source, StringComparison.Ordinal );
        Assert.Contains( "TWITTER_AVATAR_URL_PREFIX = \"https://pbs.twimg.com/profile_images\"", source, StringComparison.Ordinal );
    }

    /// <summary>The words of a Ruby <c>NAME = %w[ ... ]</c> literal.</summary>
    private static string[] WordList( string source, string constant )
    {
        var match = Regex.Match( source, $@"\b{constant} = %w\[([^\]]*)\]" );
        Assert.True( match.Success, $"{constant} not found" );

        return match.Groups[1].Value.Split( ( char[] ) [' ', '\n'], StringSplitOptions.RemoveEmptyEntries );
    }
}
