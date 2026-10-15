namespace Campfire.Application.RichText;

/// <summary>How <c>messages/_message.html.erb</c> shows a text message body.</summary>
/// <param name="Html">The presentation markup (empty when Rails' rescue rendered nothing).</param>
/// <param name="IsUnrenderable">
/// <c>message_presentation</c> raised with a message its own logging can't write, so Rails renders
/// <c>messages/_unrenderable.html.erb</c> instead.
/// </param>
public sealed record MessagePresentation( string Html, bool IsUnrenderable )
{
    /// <summary>What <c>message_presentation</c>'s rescue shows for <paramref name="error"/>.</summary>
    internal static MessagePresentation Rescued( RichTextException error ) => new( "", error.IsUnrenderable );
}
