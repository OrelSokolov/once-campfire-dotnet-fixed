using System.Net;
using System.Net.Http.Headers;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Security;

namespace Campfire.IntegrationTests.Slice;

/// <summary>The native request body bounds through the whole app on real Kestrel listeners.</summary>
public sealed class RequestBodyBoundTests
{
    private const long Limit = HttpOptions.ProductionMaxBufferedBodyBytes;

    [Fact]
    public async Task A_nonform_json_body_over_the_limit_is_413_before_parsing_and_malformed_json_under_it_is_400( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Searches );

        var over = await PostJsonBytesAsync( client, token, new byte[Limit + 1] );
        var malformed = await client.SendJsonAsync( HttpMethod.Post, Routes.UnfurlLink, token, "{not-json", accept: "application/json" );

        Assert.Equal( HttpStatusCode.RequestEntityTooLarge, over.Status );
        Assert.Equal( HttpStatusCode.BadRequest, malformed.Status );
    }

    [Fact]
    public async Task Browser_sign_in_rejects_json_as_an_unsupported_body( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( );
        var credentials = "{\"email_address\":\"david@37signals.com\",\"password\":\"secret123456\"}";

        var reply = await client.SendJsonAsync( HttpMethod.Post, Routes.Session, token, credentials, accept: "text/html" );

        Assert.Equal( HttpStatusCode.UnsupportedMediaType, reply.Status );
        Assert.DoesNotContain( BrowserAuthentication.CookieName + "=", client.CookieHeader, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task A_multipart_body_over_32_mib_is_413_with_or_without_a_declared_length( bool streaming )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( );
        var before = DatabaseSnapshot.Of( factory );
        using var multipart = new MultipartFormDataContent( );
        var file = new ByteArrayContent( new byte[FormAndMethodOverride.MaximumBodyBytes] );
        file.Headers.ContentType = new MediaTypeHeaderValue( "application/octet-stream" );
        multipart.Add( file, "attachment", "big.bin" );
        using var request = new HttpRequestMessage( HttpMethod.Post, Routes.Session )
        {
            Content = streaming ? new UnknownLengthContent( multipart ) : multipart
        };
        request.Headers.Add( BrowserHeaders.CsrfToken, token );
        if ( streaming )
        {
            // Force HTTP/1 chunked framing so Kestrel discovers the limit while reading the form.
            request.Headers.TransferEncodingChunked = true;
            Assert.Null( request.Content.Headers.ContentLength );
        }

        var reply = await client.SendAsync( request, "text/html" );

        Assert.Equal( HttpStatusCode.RequestEntityTooLarge, reply.Status );
        Assert.DoesNotContain( BrowserAuthentication.CookieName + "=", client.CookieHeader, StringComparison.Ordinal );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
    }

    [Fact]
    public async Task Browser_sign_in_accepts_a_multipart_body_past_the_nonform_limit( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( );
        var file = new FileField( "attachment", "big.bin", "application/octet-stream", new byte[Limit + 1] );

        var signedIn = await client.UploadFormAsync(
            HttpMethod.Post,
            Routes.Session,
            token,
            file,
            ("email_address", "david@37signals.com"),
            ("password", "secret123456") );

        Assert.Equal( HttpStatusCode.Found, signedIn.Status );
        Assert.Contains( BrowserAuthentication.CookieName + "=", client.CookieHeader, StringComparison.Ordinal );
    }

    /// <summary>
    /// Posts <paramref name="bytes"/> as a JSON body to the link unfurler, as the Stimulus controller does; raw bytes,
    /// so the body is exactly as long as the array.
    /// </summary>
    private static Task<SliceReply> PostJsonBytesAsync( SliceClient client, string csrfToken, byte[] bytes )
    {
        var content = new ByteArrayContent( bytes );
        content.Headers.ContentType = new MediaTypeHeaderValue( "application/json" );
        var request = new HttpRequestMessage( HttpMethod.Post, Routes.UnfurlLink ) { Content = content };
        request.Headers.Add( BrowserHeaders.CsrfToken, csrfToken );
        return client.SendAsync( request, "application/json" );
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly HttpContent _inner;

        public UnknownLengthContent( HttpContent inner )
        {
            _inner = inner;
            Headers.ContentType = inner.Headers.ContentType;
        }

        protected override bool TryComputeLength( out long length )
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync( Stream stream, TransportContext? context ) =>
            _inner.CopyToAsync( stream, context );

        protected override Task SerializeToStreamAsync( Stream stream, TransportContext? context, CancellationToken cancellationToken ) =>
            _inner.CopyToAsync( stream, context, cancellationToken );
    }
}
