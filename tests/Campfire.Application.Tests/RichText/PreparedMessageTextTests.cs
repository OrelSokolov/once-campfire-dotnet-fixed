using Campfire.Application.RichText;
using Campfire.Application.Tests.RichText.Support;

namespace Campfire.Application.Tests.RichText;

public sealed class PreparedMessageTextTests
{
    [Fact]
    public void Parsed_content_reuse_does_not_cache_resolved_mention_names( )
    {
        var resolver = new CurrentMentionResolver( );
        var context = new RichTextContext( resolver, "once.campfire.test" );
        var body = $"<p>Hello <action-text-attachment sgid=\"{ReferenceFixtures.DavidSignedGlobalId}\" content-type=\"application/vnd.campfire.mention\">" +
            $"</action-text-attachment></p>";
        var prepared = RichTextPipeline.PrepareMessage( body, context );

        Assert.Equal( "Hello @David", prepared.ToPlainText( ) );
        Assert.Contains( "David", prepared.PresentMessage( ).Html, StringComparison.Ordinal );

        resolver.User = ReferenceFixtures.David with { Name = "Renamed", Title = "Renamed – Founder" };
        Assert.Equal( "Hello @Renamed", prepared.ToPlainText( ) );
        var presentation = prepared.PresentMessage( );
        Assert.False( presentation.IsUnrenderable );
        Assert.Contains( "Renamed", presentation.Html, StringComparison.Ordinal );
        Assert.DoesNotContain( "David", presentation.Html, StringComparison.Ordinal );
    }

    private sealed class CurrentMentionResolver : IAttachableResolver
    {
        public MentionUser User { get; set; } = ReferenceFixtures.David;

        public SignedAttachableLookup LocateSigned( string signedGlobalId ) =>
            signedGlobalId == ReferenceFixtures.DavidSignedGlobalId ? SignedAttachableLookup.ForUser( User ) : SignedAttachableLookup.Invalid;

    }
}
