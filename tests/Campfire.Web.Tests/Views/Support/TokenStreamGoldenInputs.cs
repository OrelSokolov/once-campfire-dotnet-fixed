using System.Globalization;
using System.Text.Json.Nodes;
using Campfire.Web.Views;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Tests.Views.Support;

/// <summary>Maps the token-stream goldens' inputs (the Rust view models' serde shape) onto the C# view models.</summary>
internal static class TokenStreamGoldenInputs
{
    public static UserView User( JsonNode node ) =>
        new( node["id"].RequiredLong( ), node["name"].RequiredString( ), node["title"].RequiredString( ), node["avatar_url"].RequiredString( ) );

    public static List<UserView> Users( JsonNode? node ) => node!.AsArray( ).Select( user => User( user! ) ).ToList( );

    public static DateTimeOffset Time( JsonNode? node )
    {
        var text = node.RequiredString( );

        // "2026-09-26T13:01:07.148296000Z": .NET parses at most seven fractional digits.
        var dotIndex = text.IndexOf( '.', StringComparison.Ordinal );
        if ( dotIndex >= 0 && text.Length - dotIndex - 2 > 7 )
        {
            text = text[..(dotIndex + 8)] + "Z";
        }

        return DateTimeOffset.Parse( text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal );
    }

    public static RoomKind Kind( JsonNode? node ) => node.OptionalString( ) switch
    {
        "open" => RoomKind.Open,
        "closed" => RoomKind.Closed,
        "direct" => RoomKind.Direct,
        var other => throw new InvalidOperationException( $"Unknown room kind {other}." ),
    };

    public static MessageView Message( JsonNode node ) => new(
        node["id"].RequiredLong( ),
        node["client_message_id"].RequiredString( ),
        node["room_id"].RequiredLong( ),
        node["room_name"].RequiredString( ),
        User( node["creator"]! ),
        Time( node["created_at"] ),
        Time( node["updated_at"] ),
        node["all_emoji"].RequiredBool( ),
        Content( node["content"]! ),
        node["boosts"] is JsonArray boosts ? boosts.Select( boost => Boost( boost! ) ).ToList( ) : [] );

    public static List<MessageItem> MessageItems( JsonNode? node ) =>
        node!.AsArray( ).Select( message => MessageItem.Uncached( Message( message! ) ) ).ToList( );

    public static BoostView Boost( JsonNode node ) => new(
        node["id"].RequiredLong( ),
        node["updated_at"] is null ? DateTimeOffset.UnixEpoch : Time( node["updated_at"] ),
        node["message_id"].RequiredLong( ),
        node["content"].RequiredString( ),
        node["all_emoji"].RequiredBool( ),
        User( node["booster"]! ) );

    public static RoomView Room( JsonNode node ) =>
        new( node["id"].RequiredLong( ), Kind( node["kind"] ), node["name"].OptionalString( ), node["display_name"].RequiredString( ) );

    public static FormRoom FormRoom( JsonNode node ) => new( node["id"] is JsonValue id ? id.GetValue<long>( ) : null, node["name"].OptionalString( ) );

    private static MessageContent Content( JsonNode node ) => node["type"].OptionalString( ) switch
    {
        "text" => new MessageContent.Text( SafeHtml.Raw( node["html"].OptionalString( ) ) ),
        "sound" => new MessageContent.Sound(
            node["url"].RequiredString( ),
            node["image"] is JsonObject image ? new SoundImageView(
                image["src"].RequiredString( ),
                image["width"].RequiredLong( ),
                image["height"].RequiredLong( ) ) : null,
            node["text"].OptionalString( ) ),
        "attachment" => new MessageContent.Attachment( new AttachmentView(
            node["filename"].RequiredString( ),
            node["blob_path"].RequiredString( ),
            node["download_path"].RequiredString( ),
            Preview( node["preview"]! ),
            Number( node["width"] ),
            Number( node["height"] ) ) ),
        "unrenderable" => new MessageContent.Unrenderable( ),
        var other => throw new InvalidOperationException( $"Unknown content type {other}." ),
    };

    private static AttachmentPreview Preview( JsonNode node ) => node["type"].OptionalString( ) switch
    {
        "video" => new AttachmentPreview.Video( node["poster_url"].RequiredString( ) ),
        "image" => new AttachmentPreview.Image( node["thumb_url"].RequiredString( ) ),
        _ => new AttachmentPreview.File( ),
    };

    private static RubyNumber? Number( JsonNode? node )
    {
        if ( node is not JsonValue value )
        {
            return null;
        }

        return value.TryGetValue<long>( out var whole ) ? RubyNumber.Whole( whole ) : RubyNumber.Real( value.GetValue<double>( ) );
    }
}
