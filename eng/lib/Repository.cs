// The shared library of the C# scripts under bench/ and eng/ (Commands.cs, ArgumentReader.cs, JsonElementExtensions.cs and
// this file). A script includes the files it uses with `#:include`: `#:include ../eng/lib/Repository.cs` from bench/,
// `#:include lib/Repository.cs` from eng/. Commands.cs needs this file too.

/// <summary>Where the repository is, seen from the running script.</summary>
internal static class Repository
{
    /// <summary>The repository root: the parent of the directory holding the entry script (bench/ or eng/).</summary>
    public static string Root
    {
        get
        {
            var entryDirectory = ( string ) AppContext.GetData( "EntryPointFileDirectoryPath" )!;

            return Path.GetFullPath( Path.Combine( entryDirectory, ".." ) );
        }
    }

    /// <summary>Where the parity seeds are: PARITY_SEED_DIR when it is set, else parity/.seed (as bench/run reads them).</summary>
    public static string SeedDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable( "PARITY_SEED_DIR" );
            if ( !string.IsNullOrEmpty( configured ) )
            {
                return configured;
            }

            return Path.Combine( Root, "parity", ".seed" );
        }
    }
}
