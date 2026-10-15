using System.Text.Json;
using System.Text.Json.Nodes;

namespace Campfire.Tests.Shared;

/// <summary>
/// Where the test suites find the checkout: the repository root (the nearest directory above the test binaries that
/// holds <c>Campfire.slnx</c>), the Rails-generated vectors under <c>vectors/</c> (never edited by hand), and the Rails
/// app at <c>90b3300</c>. Linked into every test project.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>
    /// Points the suites at another Rails checkout (same commit) when the <c>reference</c> submodule isn't checked out.
    /// The same name <c>AssetDirectories.ReferenceEnvironmentVariable</c> reads in Campfire.Web, which
    /// Application.Tests doesn't reference.
    /// </summary>
    public const string ReferenceDirectoryVariable = "CAMPFIRE_REFERENCE_DIR";

    private const string SolutionFile = "Campfire.slnx";

    private static readonly Lazy<string> RootPath = new( FindRoot );
    private static readonly Lazy<string> ReferenceRootPath = new( FindReferenceRoot );

    /// <summary>The directory holding <c>Campfire.slnx</c>.</summary>
    public static string Root => RootPath.Value;

    /// <summary>
    /// The Rails app: <see cref="ReferenceDirectoryVariable"/> when set, else <c>&lt;repo&gt;/reference</c>. Fails
    /// (never skips) when the app isn't there, naming the fix.
    /// </summary>
    public static string ReferenceRoot => ReferenceRootPath.Value;

    /// <summary>Rails' test fixtures (<c>test/fixtures</c> in the Rails app).</summary>
    public static string RailsFixturesDirectory => ReferencePath( "test", "fixtures" );

    /// <summary>A path under the repository root, from its segments.</summary>
    public static string Combine( params string[] segments ) => Path.Combine( [Root, .. segments] );

    /// <summary>A path under <c>vectors/</c>.</summary>
    public static string VectorPath( params string[] segments ) => Path.Combine( [Root, "vectors", .. segments] );

    /// <summary>The text of a file under <c>vectors/</c>.</summary>
    public static string ReadVectorText( params string[] segments ) => File.ReadAllText( VectorPath( segments ) );

    /// <summary>A JSON file under <c>vectors/</c>; the vectors nest deeper than the parser's default limit.</summary>
    public static JsonNode ReadVectorJson( params string[] segments )
    {
        var node = JsonNode.Parse( ReadVectorText( segments ), documentOptions: new JsonDocumentOptions { MaxDepth = 256 } );

        return node ?? throw new InvalidDataException( $"vectors/{string.Join( '/', segments )} is empty." );
    }

    /// <summary>A path in the Rails app.</summary>
    public static string ReferencePath( params string[] segments ) => Path.Combine( [ReferenceRoot, .. segments] );

    /// <summary>The text of a file in the Rails app.</summary>
    public static string ReadReferenceText( params string[] segments ) => File.ReadAllText( ReferencePath( segments ) );

    private static string FindRoot( )
    {
        for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory is not null; directory = directory.Parent )
        {
            if ( File.Exists( Path.Combine( directory.FullName, SolutionFile ) ) )
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException( $"No {SolutionFile} above {AppContext.BaseDirectory}." );
    }

    private static string FindReferenceRoot( )
    {
        var configured = Environment.GetEnvironmentVariable( ReferenceDirectoryVariable );
        var root = string.IsNullOrEmpty( configured ) ? Combine( "reference" ) : configured;
        if ( !File.Exists( Path.Combine( root, "config", "routes.rb" ) ) )
        {
            throw new InvalidOperationException(
                $"The Rails reference app is not at {root}: run `git submodule update --init reference` " +
                $"or set {ReferenceDirectoryVariable} to a checkout of basecamp/once-campfire at 90b3300." );
        }

        return root;
    }
}
