using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Campfire.Web.Paths;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Support;

/// <summary>A browser-like HTTP client of a <see cref="SliceAppFactory"/>: a cookie jar, no automatic redirects.</summary>
public sealed partial class SliceClient : IDisposable
{
    public const string TurboStreamAccept = "text/vnd.turbo-stream.html, text/html, application/xhtml+xml";

    private readonly SliceAppFactory _factory;
    private readonly CookieContainer _jar = new( );
    private readonly HttpClient _http;

    public SliceClient( SliceAppFactory factory )
    {
        ArgumentNullException.ThrowIfNull( factory );

        _factory = factory;
        _http = new HttpClient( new HttpClientHandler
        {
            CookieContainer = _jar,
            UseCookies = true,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
        } )
        {
            BaseAddress = factory.BaseAddress
        };
    }

    /// <summary>The <c>Cookie</c> header this client would send, for the cable upgrade.</summary>
    public string CookieHeader => _jar.GetCookieHeader( _factory.BaseAddress );

    /// <summary>The origin browsers send on same-origin writes and WebSocket upgrades.</summary>
    public string Origin => _factory.Origin;

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Adds a cookie as another server (Rails) issued it: the raw value, escaped as Rails' <c>Set-Cookie</c> does.</summary>
    public void AddCookie( string name, string value ) => _jar.Add( _factory.BaseAddress, new Cookie( name, value ) );

    public Task<SliceReply> GetAsync( string path, string accept = "text/html,application/xhtml+xml,*/*;q=0.8" ) =>
        SendAsync( new HttpRequestMessage( HttpMethod.Get, path ), accept );

    public async Task<SliceReply> SendAsync( HttpRequestMessage request, string? accept = null )
    {
        ArgumentNullException.ThrowIfNull( request );

        if ( accept is not null )
        {
            request.Headers.TryAddWithoutValidation( "Accept", accept );
        }

        using ( request )
        {
            using var response = await _http.SendAsync( request, Cancellation );
            var wire = await response.Content.ReadAsByteArrayAsync( Cancellation );
            var body = await response.Content.ReadAsStringAsync( Cancellation );
            return new SliceReply( response.StatusCode, response.Headers, response.Content.Headers, body ) { WireBytes = wire };
        }
    }

    /// <summary>Signs in through the form, as a browser does (sessions#new, then sessions#create).</summary>
    public async Task<SliceReply> SignInAsync( string emailAddress, string password = "secret123456" )
    {
        var form = await GetAsync( Routes.NewSession );
        Assert.Equal( HttpStatusCode.OK, form.Status );

        using var request = new HttpRequestMessage( HttpMethod.Post, Routes.Session )
        {
            Content = new FormUrlEncodedContent( [
                new( "authenticity_token", FormToken( form.Body ) ),
                new( "email_address", emailAddress ),
                new( "password", password ),
            ] ),
        };
        request.Headers.Add( BrowserHeaders.FetchSite, BrowserHeaders.SameOrigin );
        request.Headers.Add( BrowserHeaders.Origin, Origin );
        return await SendAsync( request );
    }

    /// <summary>The page's <c>csrf-token</c> meta tag, which Turbo and the file uploader send as <c>X-CSRF-Token</c>.</summary>
    public static string MetaToken( string html ) => MetaTokenPattern( ).Match( html ) is { Success: true } match
        ? WebUtility.HtmlDecode( match.Groups[1].Value )
        : throw new InvalidOperationException( "The page has no csrf-token meta tag." );

    /// <summary>The first form's <c>authenticity_token</c>.</summary>
    public static string FormToken( string html ) => FormTokenPattern( ).Match( html ) is { Success: true } match
        ? WebUtility.HtmlDecode( match.Groups[1].Value )
        : throw new InvalidOperationException( "The page has no authenticity_token field." );

    /// <summary>
    /// What <c>models/file_uploader.js</c> and the composer post: multipart <c>message[...]</c> fields, the page's
    /// token in <c>X-CSRF-Token</c>. <paramref name="formToken"/> adds an <c>authenticity_token</c> field;
    /// <paramref name="origin"/> and <paramref name="fetchSite"/> (null leaves them out) are the browser's metadata.
    /// </summary>
    public async Task<SliceReply> PostMessageAsync(
        long roomId,
        string? csrfToken,
        string? body = null,
        (string Name, string Type, byte[] Bytes)? file = null,
        string? clientMessageId = null,
        string? formToken = null,
        string? origin = null,
        string? fetchSite = BrowserHeaders.SameOrigin )
    {
        var content = new MultipartFormDataContent( );
        if ( formToken is not null )
        {
            content.Add( new StringContent( formToken ), "authenticity_token" );
        }

        if ( body is not null )
        {
            content.Add( new StringContent( body ), "message[body]" );
        }

        if ( file is { } upload )
        {
            var part = new ByteArrayContent( upload.Bytes );
            part.Headers.ContentType = new MediaTypeHeaderValue( upload.Type );
            content.Add( part, "message[attachment]", upload.Name );
        }

        content.Add( new StringContent( clientMessageId ?? Guid.NewGuid( ).ToString( ) ), "message[client_message_id]" );

        var request = new HttpRequestMessage( HttpMethod.Post, Routes.RoomMessages( roomId ) ) { Content = content };
        if ( csrfToken is not null )
        {
            request.Headers.Add( BrowserHeaders.CsrfToken, csrfToken );
        }

        if ( origin is not null )
        {
            request.Headers.Add( BrowserHeaders.Origin, origin );
        }

        if ( fetchSite is not null )
        {
            request.Headers.TryAddWithoutValidation( BrowserHeaders.FetchSite, fetchSite );
        }

        return await SendAsync( request, TurboStreamAccept );
    }

    /// <summary>A cable connection with this client's cookies.</summary>
    public Task<CableTestClient> ConnectCableAsync( ) =>
        CableTestClient.ConnectAsync( _factory.CableAddress, Origin, CookieHeader );

    public void Dispose( ) => _http.Dispose( );

    [GeneratedRegex( "<meta name=\"csrf-token\" content=\"([^\"]*)\"" )]
    private static partial Regex MetaTokenPattern( );

    [GeneratedRegex( "name=\"authenticity_token\" value=\"([^\"]*)\"" )]
    private static partial Regex FormTokenPattern( );
}
