using Campfire.Web.Hosting;

namespace Campfire.Web.App;

/// <summary>
/// <c>Rails.root.join("storage")</c> and what lives under it: the database (<c>db/&lt;env&gt;.sqlite3</c>,
/// <c>config/database.yml</c>), the <c>local</c> disk service (<c>files/</c>, <c>config/storage.yml</c>) and the
/// backups (<c>backups/</c>, <c>script/admin/prepare-backup</c>). In the image the root is <c>/rails/storage</c>.
/// </summary>
/// <param name="Root">The storage root.</param>
/// <param name="Environment"><c>RAILS_ENV</c>, which names the database file.</param>
public sealed record StoragePaths( string Root, string Environment )
{
    public string DatabaseDirectory => Path.Combine( Root, "db" );

    public string DatabaseFile => Path.Combine( DatabaseDirectory, $"{Environment}.sqlite3" );

    public string Files => Path.Combine( Root, "files" );

    public string Backups => Path.Combine( Root, "backups" );

    /// <summary><c>CAMPFIRE_STORAGE_PATH</c>, else <c>storage</c> under the content root.</summary>
    public static StoragePaths From( CampfireOptions options, string contentRoot )
    {
        ArgumentNullException.ThrowIfNull( options );

        var root = options.StoragePath ?? Path.Combine( contentRoot, "storage" );
        return new StoragePaths( Path.GetFullPath( root ), options.Environment );
    }

    /// <summary>Boot step 1: <c>storage/{db,files}</c> exist before anything opens them.</summary>
    public void CreateDirectories( )
    {
        Directory.CreateDirectory( DatabaseDirectory );
        Directory.CreateDirectory( Files );
    }
}
