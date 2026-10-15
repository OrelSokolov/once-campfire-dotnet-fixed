namespace Campfire.Web.Assets;

/// <summary>
/// Where the asset pipeline reads its inputs. Rails files (<c>app/assets</c>, <c>app/javascript</c>,
/// <c>vendor/javascript</c>, <c>config/importmap.rb</c>, <c>config/initializers/assets.rb</c> and
/// <c>public/</c>) come from the pinned Rails app; gem assets come from <c>vendor/assets</c>, whose
/// <c>LOAD_PATH</c> lists Propshaft's load path. A checkout finds both beside <c>Campfire.slnx</c>;
/// the image copies them into directories it names through configuration or the environment.
/// </summary>
public static class AssetDirectories
{
    /// <summary>The Rails app's root (the <c>reference</c> submodule in a checkout).</summary>
    public const string ReferenceEnvironmentVariable = "CAMPFIRE_REFERENCE_DIR";

    /// <summary>The vendored gem assets (<c>vendor/assets</c> in a checkout).</summary>
    public const string VendorEnvironmentVariable = "CAMPFIRE_VENDOR_ASSETS_DIR";

    private const string SolutionFile = "Campfire.slnx";

    /// <summary>
    /// The Rails root: <paramref name="configured"/> when set, else <c>CAMPFIRE_REFERENCE_DIR</c>,
    /// else <c>&lt;repo&gt;/reference</c> found by walking up from <paramref name="startDirectory"/>
    /// (the app's base directory by default) to <c>Campfire.slnx</c>.
    /// </summary>
    public static string ResolveReference( string? configured = null, string? startDirectory = null ) =>
        Resolve( configured, ReferenceEnvironmentVariable, "reference", startDirectory );

    /// <summary>
    /// The vendored gem assets: <paramref name="configured"/> when set, else
    /// <c>CAMPFIRE_VENDOR_ASSETS_DIR</c>, else <c>&lt;repo&gt;/vendor/assets</c>.
    /// </summary>
    public static string ResolveVendor( string? configured = null, string? startDirectory = null ) =>
        Resolve( configured, VendorEnvironmentVariable, Path.Combine( "vendor", "assets" ), startDirectory );

    private static string Resolve( string? configured, string environmentVariable, string repositoryRelative, string? startDirectory )
    {
        if ( !string.IsNullOrWhiteSpace( configured ) )
        {
            return Path.GetFullPath( configured );
        }

        var fromEnvironment = Environment.GetEnvironmentVariable( environmentVariable );
        if ( !string.IsNullOrWhiteSpace( fromEnvironment ) )
        {
            return Path.GetFullPath( fromEnvironment );
        }

        var repository = FindRepository( startDirectory ?? AppContext.BaseDirectory );
        if ( repository is null )
        {
            throw new InvalidOperationException(
                $"Cannot locate '{repositoryRelative}': set {environmentVariable}, configure it, or run from a checkout containing {SolutionFile}." );
        }

        return Path.Combine( repository, repositoryRelative );
    }

    private static string? FindRepository( string startDirectory )
    {
        for ( var directory = new DirectoryInfo( startDirectory ); directory is not null; directory = directory.Parent )
        {
            if ( File.Exists( Path.Combine( directory.FullName, SolutionFile ) ) )
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
