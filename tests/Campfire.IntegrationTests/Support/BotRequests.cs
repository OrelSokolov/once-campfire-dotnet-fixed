using System.Net.Http.Headers;
using System.Text;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// Requests as a bot sends them to the bot API (<c>Messages::ByBotsController</c>): the raw request body, or a
/// multipart file; the key in the path is the only credential.
/// </summary>
internal static class BotRequests
{
    public static Task<SliceReply> PostRawAsync( this SliceClient client, string path, string body, string contentType = "text/plain" ) =>
        client.SendRawAsync( HttpMethod.Post, path, body, contentType );

    public static Task<SliceReply> SendRawAsync( this SliceClient client, HttpMethod method, string path, string body, string contentType = "text/plain" )
    {
        ArgumentNullException.ThrowIfNull( client );

        var content = new ByteArrayContent( Encoding.UTF8.GetBytes( body ) );
        content.Headers.ContentType = MediaTypeHeaderValue.Parse( contentType );
        return client.SendAsync( new HttpRequestMessage( method, path ) { Content = content } );
    }

    /// <summary>A multipart body with <paramref name="file"/> as the top-level <c>attachment</c> part.</summary>
    public static Task<SliceReply> PostAttachmentAsync( this SliceClient client, string path, (string Name, string Type, byte[] Bytes) file )
    {
        ArgumentNullException.ThrowIfNull( client );

        var part = new ByteArrayContent( file.Bytes );
        part.Headers.ContentType = new MediaTypeHeaderValue( file.Type );
        var content = new MultipartFormDataContent { { part, "attachment", file.Name } };
        return client.SendAsync( new HttpRequestMessage( HttpMethod.Post, path ) { Content = content } );
    }
}
