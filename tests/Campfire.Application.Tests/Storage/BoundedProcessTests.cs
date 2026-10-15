using System.Diagnostics;
using System.Globalization;
using System.Text;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Media;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary>Media subprocesses: exact argument vectors, no shell, deadlines, output caps and cleanup.</summary>
public sealed class BoundedProcessTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds( 30 );

    [Fact]
    public void The_preview_and_probe_argument_vectors_are_active_storages( )
    {
        List<string> preview = ["-i", "/tmp/ActiveStorage-7-x.mov", .. ShellwordsSplit( All["video_preview_arguments"].RequiredString( ) ), "-"];

        Assert.Equal( preview, MediaTools.PreviewArguments( "/tmp/ActiveStorage-7-x.mov" ) );
        Assert.Equal( ["-print_format", "json", "-show_streams", "-show_format", "-v", "error", "/tmp/a.mov"], MediaTools.ProbeArguments( "/tmp/a.mov" ) );
    }

    [Fact]
    public async Task Arguments_reach_the_program_verbatim_without_a_shell( )
    {
        using var output = new MemoryStream( );

        var outcome = await BoundedProcess.RunAsync(
            "printf",
            ["%s|", "a b", "$(echo x)", "'q'", "*", "; rm -rf /"],
            output,
            1024,
            Generous,
            TestContext.Current.CancellationToken );

        Assert.True( outcome.Succeeded );
        Assert.Equal( "a b|$(echo x)|'q'|*|; rm -rf /|", Encoding.UTF8.GetString( output.ToArray( ) ) );
    }

    [Fact]
    public async Task Standard_input_is_closed( )
    {
        using var output = new MemoryStream( );

        var outcome = await BoundedProcess.RunAsync( "cat", [], output, 1024, Generous, TestContext.Current.CancellationToken );

        Assert.Equal( 0, outcome.ExitCode );
        Assert.Equal( 0, output.Length );
    }

    [Fact]
    public async Task A_process_past_its_deadline_is_killed_with_its_children( )
    {
        using var output = new MemoryStream( );
        var clock = Stopwatch.StartNew( );

        var error = await Assert.ThrowsAsync<StorageException>( ( ) =>
            BoundedProcess.RunAsync(
                "sh",
                ["-c", "sleep 30 & echo $!; wait"],
                output,
                1024,
                TimeSpan.FromMilliseconds( 500 ),
                TestContext.Current.CancellationToken ) );

        Assert.Equal( StorageFailure.TimedOut, error.Failure );
        Assert.True( clock.Elapsed < TimeSpan.FromSeconds( 10 ), $"stopped after {clock.Elapsed}" );
        var child = int.Parse( Encoding.ASCII.GetString( output.ToArray( ) ).Trim( ), CultureInfo.InvariantCulture );
        Assert.False( await IsRunningAsync( child ), $"child {child} of the timed-out shell is still running" );
    }

    [Fact]
    public async Task Output_past_the_cap_stops_the_process( )
    {
        var clock = Stopwatch.StartNew( );

        var error = await Assert.ThrowsAsync<StorageException>( ( ) =>
            BoundedProcess.RunAsync( "sh", ["-c", "yes; sleep 30"], Stream.Null, 4096, Generous, TestContext.Current.CancellationToken ) );

        Assert.Equal( StorageFailure.LimitExceeded, error.Failure );
        Assert.True( clock.Elapsed < TimeSpan.FromSeconds( 10 ), $"stopped after {clock.Elapsed}" );
    }

    [Fact]
    public async Task Only_the_tail_of_standard_error_is_kept( )
    {
        var outcome = await BoundedProcess.RunAsync(
            "sh",
            ["-c", "i=0; while [ $i -lt 5000 ]; do i=$((i+1)); echo \"line $i\" >&2; done; exit 3"],
            Stream.Null,
            1024,
            Generous,
            TestContext.Current.CancellationToken );

        Assert.Equal( 3, outcome.ExitCode );
        Assert.EndsWith( "line 5000", outcome.StandardErrorTail, StringComparison.Ordinal );
        Assert.DoesNotContain( "line 1\n", outcome.StandardErrorTail, StringComparison.Ordinal );
        Assert.True( Encoding.UTF8.GetByteCount( outcome.StandardErrorTail ) <= BoundedProcess.StandardErrorTailBytes );
    }

    [Fact]
    public async Task A_missing_tool_is_file_not_found_and_does_not_succeed( )
    {
        await Assert.ThrowsAsync<FileNotFoundException>( ( ) =>
            BoundedProcess.RunAsync( "campfire-no-such-tool", [], Stream.Null, 1024, Generous, TestContext.Current.CancellationToken ) );

        Assert.False( await BoundedProcess.SucceedsAsync( "campfire-no-such-tool", ["-version"], Generous, TestContext.Current.CancellationToken ) );
        Assert.True( await BoundedProcess.SucceedsAsync( "true", [], Generous, TestContext.Current.CancellationToken ) );
        Assert.False( await BoundedProcess.SucceedsAsync( "false", [], Generous, TestContext.Current.CancellationToken ) );
    }

    [Fact]
    public async Task Cancelling_the_caller_is_a_cancellation_not_a_timeout( )
    {
        using var cancel = new CancellationTokenSource( TimeSpan.FromMilliseconds( 300 ) );

        await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => BoundedProcess.RunAsync( "sleep", ["30"], Stream.Null, 1024, Generous, cancel.Token ) );
    }

    /// <summary>Whether <paramref name="pid"/> is alive (not gone, not a zombie) after up to five seconds of polling: signals land asynchronously.</summary>
    private static async Task<bool> IsRunningAsync( int pid )
    {
        for ( var attempt = 0; attempt < 50; attempt++ )
        {
            var statPath = $"/proc/{pid}/stat";
            if ( !File.Exists( statPath ) || (await File.ReadAllTextAsync( statPath, TestContext.Current.CancellationToken )).Split( ' ' )[2] == "Z" )
            {
                return false;
            }

            await Task.Delay( 100, TestContext.Current.CancellationToken );
        }

        return true;
    }

    /// <summary><c>Shellwords.split</c> for the quoting the preview arguments use: single and double quotes, backslash escapes.</summary>
    private static List<string> ShellwordsSplit( string line )
    {
        var words = new List<string>( );
        var word = new StringBuilder( );
        var inWord = false;
        for ( var position = 0; position < line.Length; position++ )
        {
            var character = line[position];
            if ( character == '\'' )
            {
                var end = line.IndexOf( '\'', position + 1 );
                word.Append( line, position + 1, end - position - 1 );
                position = end;
                inWord = true;
            }
            else if ( character == '\\' && position + 1 < line.Length )
            {
                word.Append( line[++position] );
                inWord = true;
            }
            else if ( char.IsWhiteSpace( character ) )
            {
                if ( inWord )
                {
                    words.Add( word.ToString( ) );
                    word.Clear( );
                    inWord = false;
                }
            }
            else
            {
                word.Append( character );
                inWord = true;
            }
        }

        if ( inWord )
        {
            words.Add( word.ToString( ) );
        }

        return words;
    }
}
