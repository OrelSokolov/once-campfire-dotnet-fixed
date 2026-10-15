#!/usr/bin/env dotnet
// Exercise checkpoint rejection and the real shell resume/load guards against copied measured results.
// Usage: dotnet bench/check-resume.cs FULL_MATRIX_OUTPUT_DIR (needs AOT listener repetition 2).
#:property RestorePackagesWithLockFile=false
#:include ../eng/lib/Repository.cs
#:include ../eng/lib/Commands.cs

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

return await ResumeProof.RunAsync( args );

/// <summary>
/// The measurement settings the copied checkpoints were recorded with (bench/m3's section 10 defaults), as bench/run
/// passes them to resume-result.cs and to its own guards.
/// </summary>
internal sealed record ResumeSettings(
    string Routes,
    string Concurrencies,
    string CableClients,
    string UploadRepetitions,
    string ValidateEvery,
    string LoadMax )
{
    public static ResumeSettings Section10 { get; } = new(
        "room_show messages_page sidebar search avatar static_css up post_message",
        "1 16 64",
        "100 1000 5000 10000",
        "5",
        "1",
        "1.5" );

    /// <summary>resume-result.cs's command line for one checkpoint: FILE APP REP SUITES, then these settings.</summary>
    public string[] ResumeResultArguments( string script, string file, string app, string repetition, string suites ) =>
        [script, file, app, repetition, suites, Routes, Concurrencies, CableClients, UploadRepetitions, ValidateEvery, LoadMax];
}

/// <summary>
/// Runs resume-result.cs and the pieces of bench/run that decide whether a measured repetition is reused against copies
/// of a full matrix's checkpoints, each with the exit code it must give.
/// </summary>
internal sealed class ResumeProof
{
    /// <summary>The checkpoint the mutations start from: the AOT listener's repetition 2.</summary>
    private const string CheckpointApp = "dotnet-aot-listener";

    private const string CheckpointRepetition = "2";

    private const string CoreSuites = "http cable";

    private const string UploadSuites = "http cable upload";

    /// <summary>bench/run's logger, stubbed so its snippets run alone.</summary>
    private const string LogStub = "log() { printf '%s\\n' \"$*\"; }; ";

    private readonly string _repository = Repository.Root;
    private readonly string _output;
    private readonly string _temporary;
    private readonly string _source;
    private readonly ResumeSettings _settings = ResumeSettings.Section10;

    private ResumeProof( string output )
    {
        _output = output;
        _temporary = Path.Combine( "/tmp", $"campfire-resume-proof-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( _temporary );
        _source = Path.Combine( output, "listener", $"{CheckpointApp}-{CheckpointRepetition}.json" );
    }

    private string ResumeResultScript => Path.Combine( _repository, "bench", "resume-result.cs" );

    public static async Task<int> RunAsync( string[] args )
    {
        if ( args.Length != 1 || !Directory.Exists( args[0] ) )
        {
            Console.Error.WriteLine( "Usage: check-resume.cs FULL_MATRIX_OUTPUT_DIR" );
            return 2;
        }

        var proof = new ResumeProof( Path.GetFullPath( args[0] ) );
        await proof.CheckMutationsRejectedAsync( );
        var shell = File.ReadAllText( Path.Combine( proof._repository, "bench", "run" ) );
        await proof.CheckFailureEvidenceAsync( shell );
        await proof.CheckMetadataGuardAsync( shell );
        await proof.CheckQuietHostGuardAsync( shell );
        await proof.CheckReuseControlFlowAsync( shell );
        await proof.ClassifyCheckpointsAsync( );

        Console.WriteLine( "Resume proof passed; mutations and quiet-host timeout were rejected." );
        return 0;
    }

    /// <summary>resume-result.cs accepts the copied checkpoint, and rejects it after each mutation.</summary>
    private async Task CheckMutationsRejectedAsync( )
    {
        await CheckAsync( "valid", ReadJson( _source ), 0 );
        await MutateAsync( "missing HTTP row", root => root["http"]!.AsArray( ).RemoveAt( 0 ) );
        await MutateAsync( "duplicate HTTP row", root => root["http"]!.AsArray( ).Add( root["http"]![0]!.DeepClone( ) ) );
        await MutateAsync( "wrong repetition", root => root["rep"] = 3 );
        await MutateAsync( "HTTP error", root => root["http"]![0]!["errors"] = 1 );
        await MutateAsync( "negative CPU", root => root["http"]![0]!["server_cpu"]!["cpu_ms_per_response"] = -1 );
        await MutateAsync( "unchecked room body", root => root["http"]![0]!["validation"]!["bodies_checked"] = 0 );
        await MutateAsync( "Cable missing delivery", root => root["cable"]![0]!["verification"]!["missing"] = 1 );
        await MutateAsync( "missing Cable scale", root => root["cable"]!.AsArray( ).RemoveAt( 0 ) );
        await MutateAsync( "busy host", root => root["loadavg_start"] = "1.5 0 0" );
    }

    /// <summary>
    /// bench/run's teardown and EXIT trap keep a failed runner's evidence (exit status, container log and state, partial
    /// diagnostics) in failed/, and a successful runner adds nothing there.
    /// </summary>
    private async Task CheckFailureEvidenceAsync( string shell )
    {
        var teardown = ShellSource( shell, @"(?ms)^teardown\(\) \{\n.*?^\}", "Benchmark teardown" );
        var exitTrap = ShellSource( shell, @"(?m)^trap .* EXIT.*$", "Benchmark teardown" );

        var directory = Path.Combine( _temporary, "failure-evidence" );
        var diagnostic = Path.Combine( directory, "work", "dotnet-aot-counters", "diag" );
        Directory.CreateDirectory( diagnostic );
        File.WriteAllText( Path.Combine( diagnostic, "partial.nettrace" ), "partial trace bytes" );
        var runner = LogStub
            + "docker() { case \"$1\" in inspect) printf '{\"State\":{\"ExitCode\":139}}';; logs) printf 'crash log';; rm) :;; esac; }; "
            + $"WORK=\"$OUT/work\"; CONTAINER=fixture; ACTIVE_APP=dotnet-aot-counters; ACTIVE_REP=1; {teardown}\n"
            + $"({exitTrap}\n";
        await RunExpectingAsync( "bash", ["-c", runner + "false); status=$?; test \"$status\" = 1"], 0, "failed runner exit retained", directory );
        if ( !Directory.Exists( Path.Combine( directory, "failed" ) ) )
        {
            throw new InvalidDataException( "Failed runner discarded diagnostic evidence" );
        }

        var evidence = Directory.EnumerateDirectories( Path.Combine( directory, "failed" ) ).Single( );
        if ( !EvidenceRetained( evidence ) )
        {
            throw new InvalidDataException( "Failed runner discarded diagnostic evidence" );
        }

        await RunExpectingAsync( "bash", ["-c", runner + "true)"], 0, "successful runner creates no failure archive", directory );
        if ( Directory.EnumerateDirectories( Path.Combine( directory, "failed" ) ).Count( ) != 1
            || File.ReadAllText( Path.Combine( evidence, "runner-exit.txt" ) ).Trim( ) != "1" )
        {
            throw new InvalidDataException( "Successful runner produced a failure archive" );
        }
    }

    /// <summary>The failed runner's exit status, container log, container state and partial trace, as the fixture made them.</summary>
    private static bool EvidenceRetained( string evidence ) =>
        File.ReadAllText( Path.Combine( evidence, "runner-exit.txt" ) ).Trim( ) == "1"
        && File.ReadAllText( Path.Combine( evidence, "container.log" ) ) == "crash log"
        && ReadJson( Path.Combine( evidence, "container.json" ) )["State"]!["ExitCode"]!.GetValue<int>( ) == 139
        && File.ReadAllText( Path.Combine( evidence, "diag", "partial.nettrace" ) ) == "partial trace bytes";

    /// <summary>bench/run's resume guard accepts the saved settings and refuses a different workload.</summary>
    private async Task CheckMetadataGuardAsync( string shell )
    {
        var metadataGuard = ShellSource(
            shell,
            @"(?ms)^if \[ ""\$\{RESUME_COMPLETED:-0\}"" = 1 \] && \[ -f ""\$OUT/env.txt"" \]; then\n.*?^fi$",
            "Resume metadata guard" );
        var savedEnvironment = File.ReadAllText( Path.Combine( _output, "listener", "env.txt" ) );
        var nextEnvironment = Path.Combine( _temporary, "env-next.txt" );
        File.WriteAllText( Path.Combine( _temporary, "env.txt" ), savedEnvironment );
        File.WriteAllText( nextEnvironment, savedEnvironment.Replace( "date: 2026-09-30", "date: 2026-10-01", StringComparison.Ordinal ) );
        await RunExpectingAsync( "bash", ["-c", LogStub + metadataGuard], 0, "matching resume metadata" );
        File.WriteAllText( nextEnvironment, savedEnvironment.Replace( "http secs: 8", "http secs: 1", StringComparison.Ordinal ) );
        await RunExpectingAsync( "bash", ["-c", LogStub + metadataGuard], 1, "different resume workload" );
    }

    /// <summary>bench/run's wait_for_quiet passes below LOAD_MAX and times out above it.</summary>
    private async Task CheckQuietHostGuardAsync( string shell )
    {
        var waitForQuiet = ShellSource( shell, @"(?ms)^wait_for_quiet\(\) \{\n.*?^\}", "Quiet-host function" );
        var load = Path.Combine( _temporary, "loadavg" );
        var function = waitForQuiet.Replace( "/proc/loadavg", load, StringComparison.Ordinal );
        var script = $"LOAD_MAX=1.5; LOAD_WAIT_SECS=0; {LogStub}{function}; wait_for_quiet";
        File.WriteAllText( load, "0.5 0 0 1/1 1\n" );
        await RunExpectingAsync( "bash", ["-c", script], 0, "quiet host" );
        File.WriteAllText( load, "2.0 0 0 1/1 1\n" );
        await RunExpectingAsync( "bash", ["-c", script], 1, "busy host timeout" );
    }

    /// <summary>
    /// bench/run's run_rep, up to where it would measure: it reuses a valid checkpoint, archives one whose upload is
    /// superseded and measures again, and refuses an invalid one.
    /// </summary>
    private async Task CheckReuseControlFlowAsync( string shell )
    {
        var resumePrefix = ShellSource( shell, @"(?ms)^run_rep\(\) \{\n.*?^  wait_for_quiet\n", "Resume control flow" );
        await CheckCheckpointReuseAsync( "reused", ReadJson( _source ), 0, false, resumePrefix );

        var oldUpload = ReadJson( Path.Combine( _output, "container", "dotnet-aot-1.json" ) );
        foreach ( var row in oldUpload["upload"]!["runs"]!.AsArray( ) )
        {
            row!.AsObject( ).Remove( "thumb_url" );
            row.AsObject( ).Remove( "thumb_raster" );
        }

        await CheckCheckpointReuseAsync( "superseded upload", oldUpload, 0, true, resumePrefix );
        var incomplete = ReadJson( _source );
        incomplete["http"]!.AsArray( ).RemoveAt( 0 );
        await CheckCheckpointReuseAsync( "invalid core checkpoint", incomplete, 1, false, resumePrefix );
    }

    /// <summary>resume-result.cs classifies every real checkpoint: valid, or (old avatar uploads) upload to repeat.</summary>
    private async Task ClassifyCheckpointsAsync( )
    {
        foreach ( var suite in new[] { "container", "listener" } )
        {
            var suites = suite == "container" ? UploadSuites : CoreSuites;
            var count = 0;
            foreach ( var file in Directory.EnumerateFiles( Path.Combine( _output, suite ), "*-*.json" ) )
            {
                var root = ReadJson( file );
                var app = root["app"]!.GetValue<string>( );
                var repetition = root["rep"]!.ToString( );
                var expected = HasUnverifiedUpload( root ) ? 3 : 0;
                var arguments = _settings.ResumeResultArguments( ResumeResultScript, file, app, repetition, suites );
                await RunExpectingAsync( "dotnet", arguments, expected, Path.GetFileName( file ) );
                count++;
            }

            Console.WriteLine( $"{suite}: {count} checkpoints classified (old avatar uploads must be repeated)" );
        }
    }

    /// <summary>Whether any upload run predates raster thumbnail verification.</summary>
    private static bool HasUnverifiedUpload( JsonNode root ) =>
        root["upload"]?["runs"] is JsonArray uploads && uploads.Any( row => row?["thumb_raster"]?.GetValue<bool>( ) != true );

    private async Task MutateAsync( string name, Action<JsonNode> mutate )
    {
        var root = ReadJson( _source );
        mutate( root );
        await CheckAsync( name, root, 1 );
    }

    /// <summary>resume-result.cs on <paramref name="root"/> as the AOT listener's repetition 2 checkpoint.</summary>
    private async Task CheckAsync( string name, JsonNode root, int expected )
    {
        var file = Path.Combine( _temporary, "checkpoint.json" );
        File.WriteAllText( file, root.ToJsonString( ) );
        var arguments = _settings.ResumeResultArguments( ResumeResultScript, file, CheckpointApp, CheckpointRepetition, CoreSuites );
        await RunExpectingAsync( "dotnet", arguments, expected, name );
    }

    /// <summary>
    /// bench/run's run_rep on <paramref name="root"/> as the only result in a fresh output directory: whether it would
    /// measure again (<paramref name="remeasure"/>), and that the checkpoint's bytes survive, in place or archived.
    /// </summary>
    private async Task CheckCheckpointReuseAsync( string name, JsonNode root, int expected, bool remeasure, string resumePrefix )
    {
        var directory = Path.Combine( _temporary, Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( directory );
        var app = root["app"]!.GetValue<string>( );
        var repetition = root["rep"]!.ToString( );
        var file = Path.Combine( directory, $"{app}-{repetition}.json" );
        var bytes = root.ToJsonString( );
        File.WriteAllText( file, bytes );
        var script = $"{LogStub}wait_for_quiet() {{ :; }}; {resumePrefix}printf 'WOULD_MEASURE\\n';\n}}\nrun_rep \"$1\" \"$2\"";
        var suites = root["upload"]!.AsObject( ).Count > 0 ? UploadSuites : CoreSuites;
        var text = await RunExpectingAsync( "bash", ["-c", script, "--", app, repetition], expected, name, directory, suites );
        if ( text.Contains( "WOULD_MEASURE", StringComparison.Ordinal ) != remeasure || File.Exists( file ) == remeasure )
        {
            throw new InvalidDataException( $"{name}: incorrect checkpoint reuse/archive control flow" );
        }

        var retained = file;
        if ( remeasure )
        {
            retained = Directory.EnumerateFiles( directory, "result.json", SearchOption.AllDirectories ).Single( );
        }

        if ( File.ReadAllText( retained ) != bytes )
        {
            throw new InvalidDataException( $"{name}: checkpoint bytes changed" );
        }
    }

    /// <summary>
    /// Runs a command in bench/run's environment (output directory <paramref name="directory"/>, default the temporary
    /// one) and checks its exit code; a rejection must come from the check under test.
    /// </summary>
    private async Task<string> RunExpectingAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int expected,
        string name,
        string? directory = null,
        string suites = CoreSuites )
    {
        var result = await Commands.CaptureAsync( executable, arguments, environment: BenchRunEnvironment( directory ?? _temporary, suites ) );
        var text = result.Output + result.Error;
        if ( result.ExitCode != expected )
        {
            throw new InvalidDataException( $"{name}: expected exit {expected}, got {result.ExitCode}: {text}" );
        }

        if ( expected != 0 && !ReachedValidation( text ) )
        {
            throw new InvalidDataException( $"{name} failed before its validation: {text}" );
        }

        Console.WriteLine( $"PASS {name}: {text.Trim( )}" );
        return text;
    }

    /// <summary>The variables bench/run's resume functions read: a completed-run resume with these settings.</summary>
    private Dictionary<string, string> BenchRunEnvironment( string output, string suites ) => new( StringComparer.Ordinal )
    {
        ["OUT"] = output,
        ["RESUME_COMPLETED"] = "1",
        ["BENCH"] = Path.Combine( _repository, "bench" ),
        ["SUITES"] = suites,
        ["HTTP_ROUTES"] = _settings.Routes,
        ["HTTP_CONCS"] = _settings.Concurrencies,
        ["CABLE_CLIENTS"] = _settings.CableClients,
        ["UPLOAD_REPS"] = _settings.UploadRepetitions,
        ["VALIDATE_EVERY"] = _settings.ValidateEvery,
        ["LOAD_MAX"] = _settings.LoadMax,
    };

    /// <summary>Whether a rejection came from a validator: resume-result.cs, bench/run's refusal, or its settings guard.</summary>
    private static bool ReachedValidation( string text ) =>
        text.Contains( "resume-result:", StringComparison.Ordinal )
        || text.Contains( "refusing an invalid measurement", StringComparison.Ordinal )
        || text.Contains( "resume settings differ", StringComparison.Ordinal );

    /// <summary>The part of bench/run's source that <paramref name="pattern"/> matches; it must be there.</summary>
    private static string ShellSource( string shell, string pattern, string what )
    {
        var match = Regex.Match( shell, pattern );
        if ( !match.Success )
        {
            throw new InvalidDataException( $"{what} missing" );
        }

        return match.Value;
    }

    private static JsonNode ReadJson( string path ) => JsonNode.Parse( File.ReadAllText( path ) )!;
}
