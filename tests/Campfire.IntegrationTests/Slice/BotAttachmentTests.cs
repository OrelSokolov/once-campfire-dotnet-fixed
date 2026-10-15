using System.Net;
using System.Text.Json.Nodes;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// The bot API's attachment strings, which <c>Messages::ByBotsController</c> hands to <c>MessagesController</c>'s code:
/// a blob's signed id attaches that blob, on create and on update, and an empty attachment on update removes the
/// message's file (<c>DeleteOne</c>), which is purged later.
/// </summary>
public sealed class BotAttachmentTests
{
    private static readonly long Watercooler = SliceAppFactory.Id( "watercooler" );

    private static readonly long Bender = SliceAppFactory.Id( "bender" );

    [Fact]
    public async Task A_bot_attaches_a_blob_by_its_signed_id_then_replaces_and_removes_it( )
    {
        await using var factory = new SliceAppFactory( );
        using var bot = factory.Client( );
        var (first, firstSignedId) = await StoredBlobs.CreateAsync( factory, "one.txt", "hello!"u8.ToArray( ), "text/plain" );
        var (second, secondSignedId) = await StoredBlobs.CreateAsync( factory, "two.txt", "world!"u8.ToArray( ), "text/plain" );
        var botKey = BotApiKeys.Bender( factory );

        var created = await SendMultipartAsync( bot, HttpMethod.Post, Routes.RoomBotMessages( Watercooler, botKey ), firstSignedId );
        Assert.Equal( HttpStatusCode.Created, created.Status );
        var id = factory.Scalar<long>( $"SELECT max(id) FROM messages WHERE creator_id = {Bender}" );
        var attachedFirst = SliceRows.AttachedBlob( factory, id ).Id;
        factory.Events.Clear( );
        var replaced = await SendMultipartAsync( bot, HttpMethod.Put, Routes.RoomBotMessage( Watercooler, botKey, id ), secondSignedId );
        var attachedSecond = SliceRows.AttachedBlob( factory, id ).Id;
        var removed = await SendMultipartAsync( bot, HttpMethod.Put, Routes.RoomBotMessage( Watercooler, botKey, id ), "" );

        Assert.Equal( HttpStatusCode.Created, created.Status );
        Assert.Equal( (first, second), (attachedFirst, attachedSecond) );
        Assert.Equal( (HttpStatusCode.OK, HttpStatusCode.OK), (replaced.Status, removed.Status) );
        Assert.Null( JsonNode.Parse( removed.Body )!["attachment"] );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM active_storage_attachments WHERE record_type = 'Message' AND record_id = {id}" ) );
        Assert.Contains( $"purge_job blob {first}", factory.Events.All );
        Assert.Contains( $"purge_job blob {second}", factory.Events.All );
    }

    /// <summary>A multipart body whose <c>attachment</c> part is the string <paramref name="signedId"/>, not a file.</summary>
    private static async Task<SliceReply> SendMultipartAsync( SliceClient bot, HttpMethod method, string path, string signedId )
    {
        using var content = new MultipartFormDataContent( );
        content.Add( new StringContent( signedId ), "attachment" );
        using var request = new HttpRequestMessage( method, path ) { Content = content };
        return await bot.SendAsync( request, "application/json" );
    }
}
