using System.Net;
using System.Text;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

public sealed class NativeFrontendContractTests
{
    [Fact]
    public async Task Form_encoded_bot_boosts_keep_the_literal_text_and_method_override_bytes( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var room = SliceAppFactory.Id( "watercooler" );
        var bot = SliceAppFactory.Id( "bender" );
        var key = BotApiKeys.Of( factory, bot );
        var created = await client.PostRawAsync( Routes.RoomBotMessages( room, key ), "bot boost target" );
        Assert.Equal( HttpStatusCode.Created, created.Status );
        var message = factory.Scalar<long>( "SELECT id FROM messages ORDER BY id DESC LIMIT 1" );
        const string text = "+&_method=put";
        var response = await client.SendAsync( new HttpRequestMessage( HttpMethod.Post, $"/Rooms/{room}/{key}/Messages/{message}/boosts" )
        {
            Content = new StringContent( text, Encoding.UTF8, "application/x-www-form-urlencoded" )
        } );
        Assert.Equal( HttpStatusCode.Created, response.Status );
        Assert.Equal( text, factory.Scalar<string>( $"SELECT content FROM boosts WHERE message_id={message} AND booster_id={bot}" ) );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE id={message}" ) );
    }

    [Fact]
    public async Task An_empty_file_part_does_not_remove_a_bots_existing_avatar( )
    {
        await using var factory = new SliceAppFactory( );
        using var admin = factory.Client( );
        admin.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        var bot = SliceAppFactory.Id( "bender" );
        var path = Routes.AccountBot( bot );
        var token = await admin.CsrfTokenAsync( Routes.EditAccountBot( bot ) );
        var (name, type, bytes) = SliceFiles.Moon( );
        var uploaded = await admin.UploadFormAsync( HttpMethod.Patch, path, token, new FileField( "user[avatar]", name, type, bytes ) );
        Assert.Equal( HttpStatusCode.Found, uploaded.Status );
        var before = AvatarBlobId( factory, bot );
        Assert.True( before > 0 );
        factory.Events.Clear( );
        var empty = await admin.UploadFormAsync( HttpMethod.Patch, path, token, new FileField( "user[avatar]", "", "application/octet-stream", [] ) );
        Assert.Equal( HttpStatusCode.Found, empty.Status );
        Assert.Equal( before, AvatarBlobId( factory, bot ) );
        Assert.Empty( factory.Events.Only( "purge_job", "upload" ) );
    }

    private static long AvatarBlobId( SliceAppFactory factory, long userId ) =>
        factory.Scalar<long>( $"SELECT blob_id FROM active_storage_attachments WHERE record_type='User' AND record_id={userId} AND name='avatar'" );
}
