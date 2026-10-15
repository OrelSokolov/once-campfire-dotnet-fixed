using Campfire.Application.RichText.Content;

namespace Campfire.Application.RichText;

/// <summary>
/// One loaded body within one resolver/lease context. Presentation filters work on a clone, so they cannot
/// change later plain text or canonical markup. Attachment resolvers remain live; resolved user names are not cached.
/// </summary>
public sealed class PreparedMessageText
{
    private readonly RichTextContent _content;
    private readonly RichTextContext _context;

    internal PreparedMessageText( RichTextContent content, RichTextContext context )
    {
        _content = content;
        _context = context;
    }

    public string ToPlainText( ) => _content.ToPlainText( _context );

    /// <summary><c>message_presentation</c> with its rescue (<see cref="RichTextPipeline.RenderPresentation"/> on a copy).</summary>
    public MessagePresentation PresentMessage( )
    {
        try
        {
            var copy = RichTextContent.FromFragment( _content.Root.DeepClone( ) );
            return new MessagePresentation( RichTextPipeline.RenderPresentation( copy, _context ), IsUnrenderable: false );
        }
        catch ( RichTextException error )
        {
            return MessagePresentation.Rescued( error );
        }
    }
}
