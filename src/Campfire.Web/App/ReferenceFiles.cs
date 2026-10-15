using Campfire.Web.Assets;
using Microsoft.Extensions.Options;

namespace Campfire.Web.App;

/// <summary>
/// Files the Rails app sends straight from its tree (<c>Rails.root.join("app/assets/images/...")</c>): the default bot
/// avatar and the stock logos. In a checkout that is the <c>reference</c> submodule; the image copies the tree and
/// names it with <c>CAMPFIRE_REFERENCE_DIR</c>.
/// </summary>
public sealed class ReferenceFiles( IOptions<AssetOptions> options )
{
    private readonly Lazy<string> _root = new( ( ) => AssetDirectories.ResolveReference( options.Value.ReferenceDirectory ) );

    /// <summary>A file under <c>app/assets/images</c>.</summary>
    public string Image( string relativePath )
    {
        ArgumentException.ThrowIfNullOrEmpty( relativePath );

        return Path.Combine( _root.Value, "app", "assets", "images", relativePath );
    }
}
