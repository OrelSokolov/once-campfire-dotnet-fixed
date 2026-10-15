using System.Text;
using Campfire.Application.Storage.Media;

namespace Campfire.Application.Tests.Storage.Support;

/// <summary>
/// Tests that compare processed media byte for byte carry <c>[Trait(Media.Name, Media.Debian)]</c>.
/// They need the libvips and ffmpeg builds the reference image ships (Debian trixie
/// <c>libvips42t64=8.16.1-1+deb13u1</c>, <c>ffmpeg=7:7.1.5-0+deb13u1</c>), which
/// <c>eng/toolchain/test</c> provides. Anywhere else they fail with instructions rather than skip;
/// leave them out with <c>dotnet test ... -- --filter-not-trait "Media=Debian"</c>.
/// </summary>
internal static class MediaToolchain
{
    public const string Trait = "Media";
    public const string Debian = "Debian";

    private static readonly Lazy<Task<string>> LocalFfmpeg = new( ( ) => FirstLineAsync( "ffmpeg", "-version" ) );

    /// <summary>Fails unless the local libvips and ffmpeg are the versions that produced the vectors.</summary>
    public static async Task RequirePinnedVersionsAsync( )
    {
        var versions = StorageVectors.All["versions"]!;
        var expectedVips = versions["libvips"].RequiredString( );
        var expectedFfmpeg = versions["ffmpeg"].RequiredString( );
        var localVips = VipsRuntime.Version;
        var localFfmpeg = await LocalFfmpeg.Value;

        Assert.True(
            localVips == expectedVips && localFfmpeg == expectedFfmpeg,
            $"""
            This test compares variants and previews byte for byte, so it needs the reference image's media libraries.
              expected: libvips {expectedVips}; {expectedFfmpeg}
              found:    libvips {localVips}; {localFfmpeg}
            Run it in the pinned toolchain with eng/toolchain/test, or leave these tests out with:
              dotnet test --project tests/Campfire.Application.Tests -c Release -- --filter-not-trait "{Trait}={Debian}"
            """ );
    }

    private static async Task<string> FirstLineAsync( string tool, string argument )
    {
        using var output = new MemoryStream( );
        try
        {
            await BoundedProcess.RunAsync( tool, [argument], output, 1 << 20, TimeSpan.FromSeconds( 30 ), CancellationToken.None );
        }
        catch ( FileNotFoundException )
        {
            return $"{tool} is not installed";
        }

        return Encoding.UTF8.GetString( output.ToArray( ) ).Split( '\n' )[0].Trim( );
    }
}
