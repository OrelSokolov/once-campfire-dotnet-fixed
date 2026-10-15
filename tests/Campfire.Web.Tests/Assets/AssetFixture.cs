using System.Text.Json.Nodes;
using Campfire.Web.Assets;

namespace Campfire.Web.Tests.Assets;

/// <summary>
/// The asset catalog built from the checkout (or <c>CAMPFIRE_REFERENCE_DIR</c>), and the golden
/// outputs the Rails app produced (<c>vectors/assets/*</c>, from <c>assets:precompile</c> and the
/// real helpers). A missing Rails tree fails the tests rather than skipping them.
/// </summary>
public sealed class AssetFixture
{
    public AssetFixture( )
    {
        Root = RepositoryPaths.Root;
        Catalog = AssetCatalog.Build(
            AssetDirectories.ResolveReference( startDirectory: Root ),
            AssetDirectories.ResolveVendor( startDirectory: Root ) );
        Paths = new AssetPaths( Catalog );
        Tags = new AssetTags( Catalog, Paths );
        StaticFiles = new StaticFiles( Catalog );
    }

    public string Root { get; }

    public AssetCatalog Catalog { get; }

    public AssetPaths Paths { get; }

    public AssetTags Tags { get; }

    public StaticFiles StaticFiles { get; }

    public static string Vector( string name ) => RepositoryPaths.ReadVectorText( "assets", name );

    public static JsonNode JsonVector( string name ) => RepositoryPaths.ReadVectorJson( "assets", name );

    public StaticResponse Get( string path )
    {
        var response = StaticFiles.Serve( new StaticRequest( "GET", path ) );
        Assert.True( response is not null, $"{path} isn't served" );

        return response;
    }
}
