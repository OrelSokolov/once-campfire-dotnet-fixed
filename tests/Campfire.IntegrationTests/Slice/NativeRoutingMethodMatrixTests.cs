using System.Net;
using System.Text;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Real wrong-method routing precedes authorization and CSRF for browser and API representations.</summary>
public sealed class NativeRoutingMethodMatrixTests
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long Fourth = SliceAppFactory.Id( "fourth" );

    private static readonly string[] Accepts = ["text/html", "application/json", SliceClient.TurboStreamAccept];

    /// <summary>
    /// A write's CSRF states, which the app judges by fetch metadata alone: none sent (accepted on plain HTTP), a
    /// cross-site request, and a page's own same-origin write.
    /// </summary>
    private static readonly string?[] FetchSites = [null, "cross-site", BrowserHeaders.SameOrigin];

    [Fact]
    public async Task Wrong_methods_return_405_before_authentication_and_csrf_without_writes( )
    {
        await using var factory = new SliceAppFactory( );
        using var anonymous = factory.Client( );
        using var signedIn = factory.Client( );
        var david = SliceAppFactory.Id( "david" );
        signedIn.SignInAs( factory, david );
        StaleSessions.Mark( factory, david );
        await signedIn.GetAsync( Routes.Room( Designers ) );
        await StaleSessions.WaitForRefreshAsync( factory, david );
        var routes = new[]
        {
            ( Method: HttpMethod.Put, Path: Routes.RoomMessages( Designers ), Allowed: new[] { "GET", "HEAD", "POST" }, Override: false ),
            ( Method: HttpMethod.Post, Path: Routes.EditMessage( Fourth ), Allowed: new[] { "GET", "HEAD" }, Override: false ),
            ( Method: HttpMethod.Post, Path: Routes.RoomMessages( Designers ), Allowed: new[] { "GET", "HEAD", "POST" }, Override: true ),
            ( Method: HttpMethod.Get, Path: Routes.AccountJoinCode, Allowed: new[] { "POST" }, Override: false ),
            ( Method: HttpMethod.Head, Path: Routes.AccountJoinCode, Allowed: new[] { "POST" }, Override: false ),
        };
        // Even a session due for an activity refresh must not take a writer lease on a rejected route.
        StaleSessions.MarkAll( factory );
        var database = DatabaseSnapshot.Of( factory );

        foreach ( var route in routes )
        {
            foreach ( var client in new[] { anonymous, signedIn } )
            {
                foreach ( var accept in Accepts )
                {
                    foreach ( var fetchSite in FetchSites )
                    {
                        using var request = CreateRequest( route.Method, route.Path, route.Override, fetchSite );
                        var response = await client.SendAsync( request, accept );

                        Assert.Equal( HttpStatusCode.MethodNotAllowed, response.Status );
                        Assert.Null( response.Location );
                        var allow = response.Raw( "Allow" ) ?? "";
                        var allowed = allow.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
                        Assert.Equal( route.Allowed.Order( StringComparer.Ordinal ), allowed.Order( StringComparer.Ordinal ) );
                        Assert.Equal( database, DatabaseSnapshot.Of( factory ) );
                    }
                }
            }
        }
    }

    /// <summary>
    /// A urlencoded write (<c>_method=delete</c> when <paramref name="methodOverride"/>, else empty) with
    /// <paramref name="fetchSite"/> as its <c>Sec-Fetch-Site</c>, or none when null.
    /// </summary>
    private static HttpRequestMessage CreateRequest( HttpMethod method, string path, bool methodOverride, string? fetchSite )
    {
        var fields = methodOverride ? "_method=delete" : "";
        var request = new HttpRequestMessage( method, path )
        {
            Content = new StringContent( fields, Encoding.UTF8, "application/x-www-form-urlencoded" )
        };
        if ( fetchSite is not null )
        {
            request.Headers.Add( BrowserHeaders.FetchSite, fetchSite );
        }

        return request;
    }
}
