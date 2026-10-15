using System.Text.Json.Nodes;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>More than a page of people: the next page's <c>Link</c>, with the query merged into sorted parameters.</summary>
public sealed class AutocompletePagingTests
{
    [Fact]
    public async Task A_full_page_links_to_the_next_one( )
    {
        using var factory = new SliceAppFactory( );
        var people = Enumerable.Range( 0, 25 ).Select( index =>
            "INSERT INTO users (name, role, status, created_at, updated_at) " +
            $"VALUES ('Zed {index:D2}', 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00')" );
        factory.Execute( string.Join( ";", people ) );
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var usersJson = Routes.WithFormat( Routes.AutocompletableUsers, "json" );

        var first = await client.GetAsync( $"{usersJson}?query=zed", "application/json" );
        var second = await client.GetAsync( $"{usersJson}?query=zed&page=2", "application/json" );

        Assert.Equal( 20, JsonNode.Parse( first.Body )!.AsArray( ).Count );
        Assert.Equal( "25", first.Raw( "X-Total-Count" ) );
        Assert.Equal( $"<{factory.BaseAddress}autocompletable/users.json?query=zed&page=2>; rel=\"next\"", first.Raw( "Link" ) );
        Assert.Equal( 5, JsonNode.Parse( second.Body )!.AsArray( ).Count );
        Assert.Null( second.Raw( "Link" ) );
    }
}
