namespace Campfire.IntegrationTests.Support;

/// <summary>Files the tests upload.</summary>
internal static class SliceFiles
{
    /// <summary><c>reference/test/fixtures/files/moon.jpg</c>.</summary>
    public static (string Name, string Type, byte[] Bytes) Moon( ) =>
        ("moon.jpg", "image/jpeg", File.ReadAllBytes( RepositoryPaths.ReferencePath( "test", "fixtures", "files", "moon.jpg" ) ));

    /// <summary>A JPEG's signature followed by garbage: identified as an image, but libvips can't decode it.</summary>
    public static (string Name, string Type, byte[] Bytes) BrokenJpeg( ) =>
        ("broken.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF but not really an image"u8.ToArray( ), .. new byte[512]]);
}
