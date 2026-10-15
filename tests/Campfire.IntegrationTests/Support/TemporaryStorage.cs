namespace Campfire.IntegrationTests.Support;

/// <summary>A storage root (<c>CAMPFIRE_STORAGE_PATH</c>) of its own for each test host.</summary>
internal static class TemporaryStorage
{
    public static string Create( )
    {
        var path = Path.Combine( Path.GetTempPath( ), "campfire-integration", Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( path );
        return path;
    }

    public static void Delete( string path )
    {
        try
        {
            Directory.Delete( path, recursive: true );
        }
        catch ( IOException )
        {
            // A leftover temporary directory does not affect other tests.
        }
        catch ( UnauthorizedAccessException )
        {
            // Same.
        }
    }
}
