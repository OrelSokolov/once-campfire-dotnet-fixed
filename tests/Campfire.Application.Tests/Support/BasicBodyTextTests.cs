namespace Campfire.Application.Tests.Support;

/// <summary>The stand-in for the rich text component behaves as Rust's <c>BasicRichText</c> tests expect.</summary>
public sealed class BasicBodyTextTests
{
    private static string? NoUsers( long id ) => null;

    [Fact]
    public void Plain_text_strips_tags( )
    {
        var bodyText = new BasicBodyText( );

        Assert.Equal( "My hovercraft is full of eels", bodyText.ToPlainText( "<span>My hovercraft is full of eels</span>", NoUsers ) );
        Assert.Equal( "Hello there", bodyText.ToPlainText( "Hello <b>there</b>", NoUsers ) );
    }

    [Fact]
    public void Plain_text_renders_mentions_with_the_current_name( )
    {
        var html = $"Hey {BasicBodyText.MentionAttachmentFor( 7 )}";

        Assert.Equal( "Hey @Kevin", new BasicBodyText( ).ToPlainText( html, id => id == 7 ? "Kevin" : null ) );
    }

    [Fact]
    public void Mentioned_user_ids_come_from_sgids_without_duplicates( )
    {
        var html = $"<div>Hey {BasicBodyText.MentionAttachmentFor( 127326141 )} {BasicBodyText.MentionAttachmentFor( 127326141 )}</div>";

        Assert.Equal( [127326141L], new BasicBodyText( ).MentionedUserIds( html ) );
    }
}
