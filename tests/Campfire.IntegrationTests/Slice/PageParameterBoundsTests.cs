using System.Net;
using System.Text.Json.Nodes;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// Page parameters at their bounds, through the app. Rails reads
/// <c>page</c> and <c>since</c> with <c>to_i</c>, which never overflows, and finds <c>before</c> and <c>after</c> by id,
/// where an id past the column's range finds nothing. The port saturates <c>page</c>, and the offset with it, at
/// <see cref="long.MaxValue"/>, and clamps <c>since</c> to the dates .NET can hold.
/// </summary>
public sealed class PageParameterBoundsTests( SharedSliceApp shared ) : IClassFixture<SharedSliceApp>
{
    /// <summary>2^62 + 1: with 20 or 500 people a page, an unchecked offset wraps to exactly 0, the first page.</summary>
    private const string WrappingPage = "4611686018427387905";

    private const string PastLong = "99999999999999999999999";

    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly string AutocompletableUsersJson = Routes.WithFormat( Routes.AutocompletableUsers, "json" );

    [Theory]
    [InlineData( WrappingPage )]
    [InlineData( PastLong )]
    [InlineData( "9223372036854775807" )]
    public async Task A_page_far_past_the_end_is_empty_not_the_first_page( string page )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );

        var people = await client.GetAsync( $"{AutocompletableUsersJson}?page={page}", "application/json" );
        var accounts = await client.GetAsync( $"{Routes.AccountUsers}?page={page}&format=turbo_stream" );

        Assert.Equal( HttpStatusCode.OK, people.Status );
        Assert.Empty( JsonNode.Parse( people.Body )!.AsArray( ) );
        Assert.NotEqual( "0", people.Raw( "X-Total-Count" ) );
        Assert.Equal( HttpStatusCode.OK, accounts.Status );
        Assert.DoesNotContain( "<li class=\"flex align-center gap margin-none", accounts.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task The_next_page_of_the_largest_page_is_itself( )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );

        var people = await client.GetAsync( $"{AutocompletableUsersJson}?page={PastLong}", "application/json" );

        Assert.Equal( $"<{shared.BaseAddress}autocompletable/users.json?page=9223372036854775807>; rel=\"next\"", people.Raw( "Link" ) );
    }

    [Theory]
    [InlineData( "before", "9223372036854775807" )]
    [InlineData( "before", "9223372036854775808" )]
    [InlineData( "after", PastLong )]
    [InlineData( "after", "-1" )]
    [InlineData( "before", "x" )]
    public async Task A_before_or_after_that_names_no_message_is_404( string key, string value )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );

        var page = await client.GetAsync( $"{Routes.RoomMessages( Designers )}?{key}={value}" );

        Assert.Equal( HttpStatusCode.NotFound, page.Status );
    }

    [Fact]
    public async Task A_since_past_either_end_of_time_is_clamped( )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );
        var refresh = Routes.RoomRefresh( Designers );

        var future = await client.GetAsync( $"{refresh}?since={PastLong}", SliceClient.TurboStreamAccept );
        var past = await client.GetAsync( $"{refresh}?since=-{PastLong}", SliceClient.TurboStreamAccept );
        var epoch = await client.GetAsync( $"{refresh}?since=0", SliceClient.TurboStreamAccept );
        var malformed = await client.GetAsync( $"{refresh}?since=not-a-timestamp", SliceClient.TurboStreamAccept );

        Assert.Equal( HttpStatusCode.OK, future.Status );
        Assert.Equal( "\n", future.Body );
        Assert.Equal( HttpStatusCode.OK, past.Status );
        Assert.StartsWith( $"<turbo-stream action=\"append\" target=\"messages_rooms_closed_{Designers}\">", past.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, malformed.Status );
        Assert.Equal( MessageIds( epoch.Body ), MessageIds( past.Body ) );
        Assert.Equal( MessageIds( epoch.Body ), MessageIds( malformed.Body ) );
    }

    /// <summary>The messages a refresh carries, in order (the bodies differ in their per-request CSRF tokens).</summary>
    private static List<string> MessageIds( string body ) =>
        [.. body.Split( "data-message-id=\"" ).Skip( 1 ).Select( rest => rest[..rest.IndexOf( '"', StringComparison.Ordinal )] )];
}
