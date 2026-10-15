namespace Campfire.Application.Storage;

/// <summary>How the storage code opens files, and the size of every buffer it copies bytes through.</summary>
public static class StorageFiles
{
    /// <summary>64 KiB: the buffer of every storage file stream and copy loop.</summary>
    public const int BufferSize = 1 << 16;

    /// <summary>Opens an existing file to read it once, front to back, asynchronously.</summary>
    public static FileStream OpenSequentialRead( string path, FileShare share = FileShare.Read ) =>
        new( path, FileMode.Open, FileAccess.Read, share, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan );

    /// <summary>Creates a file to write asynchronously; fails when the file exists.</summary>
    public static FileStream CreateNew( string path ) =>
        new( path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous );
}
