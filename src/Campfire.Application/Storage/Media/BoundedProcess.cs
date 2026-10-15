using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Campfire.Application.Storage.Media;

/// <summary>
/// Runs a media tool the way Active Storage's <c>IO.popen(argv)</c> does (an argument vector, no
/// shell) with the bounds Rails lacks: a deadline after which the whole process tree is killed, a
/// cap on standard output, and only the last <see cref="StandardErrorTailBytes"/> of standard error
/// kept. Standard input is closed at once.
/// </summary>
public static class BoundedProcess
{
    public const int StandardErrorTailBytes = 16 * 1024;

    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/>, copying standard output to
    /// <paramref name="standardOutput"/>. Throws <see cref="FileNotFoundException"/> when the tool
    /// isn't installed (Ruby's <c>Errno::ENOENT</c>), <see cref="StorageFailure.TimedOut"/> after
    /// <paramref name="timeout"/>, and <see cref="StorageFailure.LimitExceeded"/> past
    /// <paramref name="maxOutputBytes"/>; the process is gone by the time any of these surfaces.
    /// </summary>
    public static async Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Stream standardOutput,
        long maxOutputBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( arguments );
        ArgumentNullException.ThrowIfNull( standardOutput );

        using var process = Start( fileName, arguments );
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        deadline.CancelAfter( timeout );

        var errors = new TailBuffer( StandardErrorTailBytes );
        var errorPump = errors.FillAsync( process.StandardError.BaseStream, deadline.Token );
        try
        {
            await CopyBoundedAsync( process.StandardOutput.BaseStream, standardOutput, maxOutputBytes, deadline.Token );
            await errorPump;
            await process.WaitForExitAsync( deadline.Token );

            return new ProcessOutcome( process.ExitCode, errors.ToString( ) );
        }
        catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
        {
            throw new StorageException( StorageFailure.TimedOut, $"{fileName} took longer than {timeout.TotalSeconds:0.###} s and was stopped." );
        }
        finally
        {
            // Killing the tree closes its pipes, which ends the error pump too.
            await StopAsync( process );
            await ObserveAsync( errorPump );
        }
    }

    private static async Task ObserveAsync( Task pump )
    {
        try
        {
            await pump;
        }
        catch ( Exception error ) when ( error is OperationCanceledException or IOException or ObjectDisposedException )
        {
            // The pump's outcome no longer matters once the process is stopped.
        }
    }

    /// <summary><c>system(tool, *arguments, out: File::NULL, err: File::NULL)</c>: whether the tool ran and exited 0.</summary>
    public static async Task<bool> SucceedsAsync( string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken )
    {
        try
        {
            var outcome = await RunAsync( fileName, arguments, Stream.Null, long.MaxValue, timeout, cancellationToken );

            return outcome.Succeeded;
        }
        catch ( Exception error ) when ( error is FileNotFoundException or StorageException )
        {
            return false;
        }
    }

    private static Process Start( string fileName, IReadOnlyList<string> arguments )
    {
        var startInfo = new ProcessStartInfo( fileName )
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach ( var argument in arguments )
        {
            startInfo.ArgumentList.Add( argument );
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start( );
        }
        catch ( Win32Exception error )
        {
            process.Dispose( );
            throw new FileNotFoundException( $"{fileName} could not be started: {error.Message}", fileName, error );
        }

        process.StandardInput.Close( );

        return process;
    }

    private static async Task CopyBoundedAsync( Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken )
    {
        var buffer = new byte[StorageFiles.BufferSize];
        long total = 0;
        int read;
        while ( (read = await source.ReadAsync( buffer, cancellationToken )) > 0 )
        {
            total += read;
            if ( total > maxBytes )
            {
                throw new StorageException( StorageFailure.LimitExceeded, $"The tool wrote more than {maxBytes} bytes." );
            }

            await destination.WriteAsync( buffer.AsMemory( 0, read ), cancellationToken );
        }
    }

    /// <summary>Kills the process tree if it is still running, and reaps it.</summary>
    private static async Task StopAsync( Process process )
    {
        try
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
            }

            using var reap = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
            await process.WaitForExitAsync( reap.Token );
        }
        catch ( Exception error ) when ( error is InvalidOperationException or OperationCanceledException or Win32Exception )
        {
            // Already gone, or not reaped within the grace period; nothing more can be done here.
        }
    }

    /// <summary>The last bytes of a stream, decoded as UTF-8 for error messages.</summary>
    private sealed class TailBuffer( int capacity )
    {
        private readonly Queue<byte> _bytes = new( capacity );

        public async Task FillAsync( Stream source, CancellationToken cancellationToken )
        {
            var buffer = new byte[4096];
            int read;
            while ( (read = await source.ReadAsync( buffer, cancellationToken )) > 0 )
            {
                for ( var index = 0; index < read; index++ )
                {
                    if ( _bytes.Count == capacity )
                    {
                        _bytes.Dequeue( );
                    }

                    _bytes.Enqueue( buffer[index] );
                }
            }
        }

        public override string ToString( ) => Encoding.UTF8.GetString( [.. _bytes] ).TrimEnd( '\r', '\n' );
    }
}
