using System.Text;

using Campfire.Application.Data.Hooks;

namespace Campfire.Application.Tests.Support;

/// <summary>
/// A stand-in for the rich text component (Rust <c>BasicRichText</c> at 3fe9f61): tag stripping, mentions rendered as
/// <c>@Name</c>, and unverified SGID decoding. Campfire accepts unverified user SGIDs
/// (<c>lib/rails_ext/action_text_attachables.rb</c>); the exact plain-text rules belong to the rich text component.
/// </summary>
internal sealed class BasicBodyText : IMessageBodyText
{
    private const string AttachmentTag = "action-text-attachment";

    private const string AttachmentEnd = "</action-text-attachment>";

    private static readonly string[] BlockTags = ["br", "p", "div", "li", "h1", "blockquote", "pre"];

    public string ToPlainText( string storedBody, Func<long, string?> userName )
    {
        var text = new StringBuilder( );
        var rest = storedBody;
        while ( rest.IndexOf( '<', StringComparison.Ordinal ) is var start and >= 0 )
        {
            text.Append( rest[..start] );
            var end = rest.IndexOf( '>', start );
            if ( end < 0 )
            {
                rest = "";
                break;
            }

            var tag = rest[(start + 1)..end];
            var closing = tag.StartsWith( '/' );
            var name = new string( [.. tag.TrimStart( '/' ).TakeWhile( character => char.IsAsciiLetterOrDigit( character ) || character == '-' )] );
            if ( name == AttachmentTag && !closing )
            {
                if ( AttributeValues( tag, "sgid" ).FirstOrDefault( ) is { } signedGlobalId
                    && UserIdFromSignedGlobalId( signedGlobalId ) is { } id
                    && userName( id ) is { } mentioned )
                {
                    text.Append( '@' ).Append( mentioned );
                }

                var close = rest.IndexOf( AttachmentEnd, start, StringComparison.Ordinal );
                if ( close >= 0 )
                {
                    rest = rest[(close + AttachmentEnd.Length)..];
                    continue;
                }
            }

            if ( BlockTags.Contains( name ) && text.Length > 0 && !closing )
            {
                text.Append( '\n' );
            }

            rest = rest[(end + 1)..];
        }

        text.Append( rest );
        return DecodeEntities( text.ToString( ).Trim( ) );
    }

    public IReadOnlyList<long> MentionedUserIds( string storedBody ) =>
        [.. AttributeValues( storedBody, "sgid" ).Select( UserIdFromSignedGlobalId ).OfType<long>( ).Distinct( )];

    /// <summary>A mention attachment with an unsigned SGID, shaped like <c>MentionTestHelper#mention_attachment_for</c>.</summary>
    public static string MentionAttachmentFor( long userId )
    {
        var globalId = $"{{\"_rails\":{{\"data\":\"gid://campfire/User/{userId}\",\"pur\":\"attachable\"}}}}";
        var payload = Convert.ToBase64String( Encoding.UTF8.GetBytes( globalId ) );
        return $"<action-text-attachment sgid=\"{payload}--unsigned\" content-type=\"application/vnd.campfire.mention\"></action-text-attachment>";
    }

    private static List<string> AttributeValues( string html, string attribute )
    {
        var needle = $"{attribute}=\"";
        var values = new List<string>( );
        for ( var start = html.IndexOf( needle, StringComparison.Ordinal ); start >= 0; start = html.IndexOf( needle, start, StringComparison.Ordinal ) )
        {
            start += needle.Length;
            var end = html.IndexOf( '"', start );
            if ( end < 0 )
            {
                break;
            }

            values.Add( html[start..end] );
            start = end;
        }

        return values;
    }

    private static long? UserIdFromSignedGlobalId( string signedGlobalId )
    {
        var message = signedGlobalId.Split( "--" )[0].Replace( "%3D", "=", StringComparison.Ordinal );
        try
        {
            var text = Encoding.UTF8.GetString( Convert.FromBase64String( message ) );
            const string Marker = "gid://campfire/User/";
            var markerPosition = text.IndexOf( Marker, StringComparison.Ordinal );
            var digits = markerPosition < 0 ? "" : new string( [.. text[(markerPosition + Marker.Length)..].TakeWhile( char.IsAsciiDigit )] );
            return long.TryParse( digits, out var id ) ? id : null;
        }
        catch ( FormatException )
        {
            return null;
        }
    }

    private static string DecodeEntities( string text ) => text
        .Replace( "&nbsp;", " ", StringComparison.Ordinal )
        .Replace( "&lt;", "<", StringComparison.Ordinal )
        .Replace( "&gt;", ">", StringComparison.Ordinal )
        .Replace( "&quot;", "\"", StringComparison.Ordinal )
        .Replace( "&#39;", "'", StringComparison.Ordinal )
        .Replace( "&amp;", "&", StringComparison.Ordinal );
}
