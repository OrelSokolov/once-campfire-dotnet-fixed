using System.Net;
using System.Text.Json.Nodes;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>Collection-shaped search fields are not converted to legacy Ruby search strings.</summary>
public sealed class AutocompleteCollectionTests
{
    [Theory]
    [InlineData( "query[]=j&query[]=x", "prefix [\"j\", \"x\"] suffix" )]
    [InlineData( "query[a]=j", "prefix {\"a\" => \"j\"} suffix" )]
    [InlineData( "filter[]=j&filter[]=x&query=unused", "prefix [\"j\", \"x\"] suffix" )]
    public async Task Collection_fields_leave_the_scalar_search_unset( string query, string expectedName )
    {
        using var factory = new SliceAppFactory( );
        factory.Execute(
            "INSERT INTO users (name, role, status, created_at, updated_at) " +
            $"VALUES ('{expectedName}', 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00')" );
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );

        var reply = await client.GetAsync( $"{Routes.WithFormat( Routes.AutocompletableUsers, "json" )}?{query}", "application/json" );

        Assert.Equal( HttpStatusCode.OK, reply.Status );
        var users = JsonNode.Parse( reply.Body )!.AsArray( );
        Assert.Equal( 6, users.Count );
        Assert.Contains( users, user => user!["name"]!.GetValue<string>( ) == WebUtility.HtmlEncode( expectedName ) );
        Assert.Contains( users, user => user!["name"]!.GetValue<string>( ) == "David" );
    }
}
