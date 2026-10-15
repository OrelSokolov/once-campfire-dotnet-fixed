// Child processes for the C# scripts under bench/ and eng/, which drive git, docker, sqlite3, bash, dotnet and the
// imported bash harness (bench/run). Part of the shared script library (see Repository.cs, which it needs): include both
// with `#:include ../eng/lib/Commands.cs` from bench/, `#:include lib/Commands.cs` from eng/.

using System.Diagnostics;
using System.Text;

/// <summary>The result of a captured command: its exit code and everything it printed.</summary>
internal sealed record CommandResult( int ExitCode, string Output, string Error )
{
    public bool Succeeded => ExitCode == 0;

    public string Trimmed => Output.Trim( );
}

/// <summary>
/// Runs child processes, with no shell and, unless told otherwise, in the repository root: capturing their output,
/// capturing it line by line, or passing it through (optionally into a log too).
/// </summary>
internal static class Commands
{
    /// <summary>Runs a command to completion and returns its exit code, its standard output and its standard error.</summary>
    public static async Task<CommandResult> CaptureAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null )
    {
        var start = StartInfo( file, arguments, workingDirectory, environment );
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        using var process = Process.Start( start )!;
        var output = process.StandardOutput.ReadToEndAsync( );
        var error = process.StandardError.ReadToEndAsync( );
        await process.WaitForExitAsync( ).ConfigureAwait( false );

        return new CommandResult( process.ExitCode, await output.ConfigureAwait( false ), await error.ConfigureAwait( false ) );
    }

    /// <summary>
    /// Runs a command to completion and returns its exit code and every line it printed, standard output and standard
    /// error interleaved in the order they arrived.
    /// </summary>
    public static async Task<(int ExitCode, IReadOnlyList<string> Lines)> CaptureLinesAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null )
    {
        var start = StartInfo( file, arguments, workingDirectory );
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        var lines = new List<string>( );
        using var process = Process.Start( start )!;
        process.OutputDataReceived += ( _, line ) => Collect( lines, line.Data );
        process.ErrorDataReceived += ( _, line ) => Collect( lines, line.Data );
        process.BeginOutputReadLine( );
        process.BeginErrorReadLine( );
        await process.WaitForExitAsync( ).ConfigureAwait( false );

        lock ( lines )
        {
            return (process.ExitCode, lines.ToList( ));
        }
    }

    /// <summary>Runs a command with its output shown as it comes, and also appended to <paramref name="log"/>.</summary>
    public static async Task<int> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? log = null )
    {
        var start = StartInfo( file, arguments, workingDirectory, environment );
        if ( log is null )
        {
            using var inherited = Process.Start( start )!;
            await inherited.WaitForExitAsync( ).ConfigureAwait( false );

            return inherited.ExitCode;
        }

        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        var writer = new StreamWriter( log, append: true, Encoding.UTF8 ) { AutoFlush = true };
        await using ( writer.ConfigureAwait( false ) )
        {
            using var process = Process.Start( start )!;
            process.OutputDataReceived += ( _, line ) => Tee( writer, line.Data, Console.Out );
            process.ErrorDataReceived += ( _, line ) => Tee( writer, line.Data, Console.Error );
            process.BeginOutputReadLine( );
            process.BeginErrorReadLine( );
            await process.WaitForExitAsync( ).ConfigureAwait( false );

            return process.ExitCode;
        }
    }

    /// <summary>Captures a command and fails with its error output when it fails.</summary>
    public static async Task<string> RequireAsync( string file, IReadOnlyList<string> arguments, string? workingDirectory = null )
    {
        var result = await CaptureAsync( file, arguments, workingDirectory ).ConfigureAwait( false );
        if ( !result.Succeeded )
        {
            throw new InvalidOperationException( $"{file} {string.Join( ' ', arguments )} failed ({result.ExitCode}): {result.Error.Trim( )}" );
        }

        return result.Trimmed;
    }

    /// <summary>
    /// How every command here starts: no shell, in <paramref name="workingDirectory"/> (default: the repository root),
    /// with <paramref name="environment"/> set over this process's own. Long-running children (a server under test) start
    /// from it too.
    /// </summary>
    public static ProcessStartInfo StartInfo(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null )
    {
        var start = new ProcessStartInfo( file, arguments )
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Repository.Root,
        };
        foreach ( var (name, value) in environment ?? new Dictionary<string, string>( ) )
        {
            start.Environment[name] = value;
        }

        return start;
    }

    private static void Collect( List<string> lines, string? line )
    {
        if ( line is null )
        {
            return;
        }

        lock ( lines )
        {
            lines.Add( line );
        }
    }

    private static void Tee( StreamWriter log, string? line, TextWriter console )
    {
        if ( line is null )
        {
            return;
        }

        lock ( log )
        {
            log.WriteLine( line );
            console.WriteLine( line );
        }
    }
}
