using System.Net.Http.Headers;
using System.Text;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// Writes as the pages send them: urlencoded and multipart forms the way Turbo submits them (the page's CSRF token in
/// <c>X-CSRF-Token</c>, same-origin fetch metadata), JSON the way the Stimulus controllers post it, and the plain form
/// post a page makes without Turbo.
/// </summary>
public static class FormRequests
{
    private const string HtmlAccept = "text/html,application/xhtml+xml";

    /// <summary>A page's <c>csrf-token</c> meta tag, for this client's session.</summary>
    public static async Task<string> CsrfTokenAsync( this SliceClient client, string page = Routes.NewSession )
    {
        ArgumentNullException.ThrowIfNull( client );

        var reply = await client.GetAsync( page );
        return SliceClient.MetaToken( reply.Body );
    }

    /// <summary>
    /// Submits <paramref name="fields"/> urlencoded with <paramref name="method"/>, as Turbo does: the token (none when
    /// <paramref name="token"/> is null) in <c>X-CSRF-Token</c>, then <paramref name="headers"/>, then
    /// <c>Sec-Fetch-Site: same-origin</c> and the client's <c>Origin</c> unless <paramref name="headers"/> set them.
    /// </summary>
    public static Task<SliceReply> SubmitFormAsync(
        this SliceClient client,
        HttpMethod method,
        string path,
        string? token,
        IEnumerable<(string Name, string Value)>? fields = null,
        string accept = SliceClient.TurboStreamAccept,
        params (string Name, string Value)[] headers )
    {
        ArgumentNullException.ThrowIfNull( client );

        var request = new HttpRequestMessage( method, path ) { Content = UrlEncoded( fields ?? [] ) };
        return SendFromPageAsync( client, request, token, accept, headers );
    }

    /// <summary>
    /// Submits a multipart form with <paramref name="file"/> and <paramref name="fields"/>, with the headers
    /// <see cref="SubmitFormAsync"/> sends.
    /// </summary>
    public static Task<SliceReply> UploadFormAsync(
        this SliceClient client,
        HttpMethod method,
        string path,
        string? token,
        FileField file,
        params IEnumerable<(string Name, string Value)> fields )
    {
        ArgumentNullException.ThrowIfNull( client );
        ArgumentNullException.ThrowIfNull( file );

        var content = new MultipartFormDataContent( );
        foreach ( var (name, value) in fields )
        {
            content.Add( new StringContent( value ), name );
        }

        var part = new ByteArrayContent( file.Bytes );
        part.Headers.ContentType = new MediaTypeHeaderValue( file.ContentType );
        part.Headers.ContentDisposition = new ContentDispositionHeaderValue( "form-data" )
        {
            Name = file.Field,
            // An empty property value removes the parameter; browsers send an explicit quoted empty filename.
            FileName = file.FileName.Length == 0 ? "\"\"" : file.FileName,
            FileNameStar = file.FileName.Length == 0 ? null : file.FileName,
        };
        content.Add( part );

        var request = new HttpRequestMessage( method, path ) { Content = content };
        return SendFromPageAsync( client, request, token, SliceClient.TurboStreamAccept, [] );
    }

    /// <summary>
    /// Sends <paramref name="json"/> as the Stimulus controllers post it (<c>notifications_controller.js</c>,
    /// <c>unfurl_controller.js</c>): the token (none when <paramref name="token"/> is null) in <c>X-CSRF-Token</c>, then
    /// <paramref name="headers"/>; no fetch metadata.
    /// </summary>
    public static Task<SliceReply> SendJsonAsync(
        this SliceClient client,
        HttpMethod method,
        string path,
        string? token,
        string json,
        string? accept = null,
        params (string Name, string Value)[] headers )
    {
        ArgumentNullException.ThrowIfNull( client );

        var request = new HttpRequestMessage( method, path ) { Content = new StringContent( json, Encoding.UTF8, "application/json" ) };
        AddTokenAndHeaders( request, token, headers );
        return client.SendAsync( request, accept );
    }

    /// <summary>
    /// A form posted without Turbo: the token in the <c>authenticity_token</c> field (as <c>form_with</c> renders it), an
    /// HTML <c>Accept</c>, and no fetch metadata or <c>Origin</c>.
    /// </summary>
    public static Task<SliceReply> PostFormWithBodyTokenAsync( this SliceClient client, string path, string token, params (string Name, string Value)[] fields )
    {
        ArgumentNullException.ThrowIfNull( client );

        var request = new HttpRequestMessage( HttpMethod.Post, path ) { Content = UrlEncoded( [("authenticity_token", token), .. fields] ) };
        return client.SendAsync( request, HtmlAccept );
    }

    private static FormUrlEncodedContent UrlEncoded( IEnumerable<(string Name, string Value)> fields ) =>
        new( fields.Select( field => new KeyValuePair<string, string>( field.Name, field.Value ) ) );

    /// <summary>The token and <paramref name="headers"/>, then the same-origin metadata a page's fetch carries unless they set it.</summary>
    private static Task<SliceReply> SendFromPageAsync(
        SliceClient client, HttpRequestMessage request, string? token, string accept, (string Name, string Value)[] headers )
    {
        AddTokenAndHeaders( request, token, headers );
        if ( !request.Headers.Contains( BrowserHeaders.FetchSite ) )
        {
            request.Headers.Add( BrowserHeaders.FetchSite, BrowserHeaders.SameOrigin );
        }

        if ( !request.Headers.Contains( BrowserHeaders.Origin ) )
        {
            request.Headers.Add( BrowserHeaders.Origin, client.Origin );
        }

        return client.SendAsync( request, accept );
    }

    private static void AddTokenAndHeaders( HttpRequestMessage request, string? token, (string Name, string Value)[] headers )
    {
        if ( token is not null )
        {
            request.Headers.Add( BrowserHeaders.CsrfToken, token );
        }

        foreach ( var (name, value) in headers )
        {
            request.Headers.TryAddWithoutValidation( name, value );
        }
    }
}
