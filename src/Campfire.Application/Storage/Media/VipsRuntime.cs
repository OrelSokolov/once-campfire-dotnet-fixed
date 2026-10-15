using System.Globalization;
using NetVips;
using VipsGlobal = NetVips.NetVips;

namespace Campfire.Application.Storage.Media;

/// <summary>
/// libvips as <c>config/initializers/vips.rb</c> leaves it: <c>Vips.block_untrusted(true)</c> (no
/// unfuzzed loaders such as ImageMagick, so BMP, ICO and PSD aren't readable) and
/// <c>Vips.block("VipsForeignLoadOpenslide", true)</c>. NetVips binds the system libvips (no
/// <c>NetVips.Native</c>), so variants are byte-identical only against the same libvips build as the
/// reference image (Debian trixie <c>libvips42t64</c> 8.16.1-1+deb13u1).
/// </summary>
public static class VipsRuntime
{
    private static readonly Lazy<string> Initialization = new( Initialize, LazyThreadSafetyMode.ExecutionAndPublication );

    /// <summary><c>Vips.version_string</c>, e.g. <c>8.16.1</c>. Reading it initializes libvips.</summary>
    public static string Version => Initialization.Value;

    /// <summary>Loads libvips and applies the loader restrictions; call at startup, before any image work.</summary>
    public static void EnsureInitialized( ) => _ = Initialization.Value;

    private static string Initialize( )
    {
        if ( !ModuleInitializer.VipsInitialized )
        {
            throw new StorageException(
                StorageFailure.Vips, $"libvips could not be loaded: {ModuleInitializer.Exception?.Message}", ModuleInitializer.Exception );
        }

        VipsGlobal.BlockUntrusted = true;
        Operation.Block( "VipsForeignLoadOpenslide", true );

        // The operation cache would keep every processed temp file's image (and file descriptor)
        // alive for reuse that never comes: each copy has a fresh name. Output is unaffected.
        Cache.Max = 0;

        return string.Create( CultureInfo.InvariantCulture, $"{VipsGlobal.Version( 0 )}.{VipsGlobal.Version( 1 )}.{VipsGlobal.Version( 2 )}" );
    }
}
