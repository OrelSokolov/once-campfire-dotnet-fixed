#!/usr/bin/env dotnet
// Validate a completed repetition before bench/run reuses it. Metadata must match before this runs.
// Usage: resume-result.cs FILE APP REP SUITES ROUTES CONCS CABLE_CLIENTS UPLOAD_REPS VALIDATE_EVERY LOAD_MAX
// Exit codes: 0 complete and valid (bench/run reuses it), 1 invalid or unreadable (bench/run stops), 2 bad usage, 3 valid
// except for upload results that must be measured again (bench/run archives the checkpoint and repeats the repetition).
#:property RestorePackagesWithLockFile=false
#:include ../eng/lib/JsonElementExtensions.cs

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

return ResumeResult.Run( args );

/// <summary>
/// The repetition bench/run asks about, and the settings it must have been measured with. Numbers stay text until a
/// check reads them, so a bad one fails that check (an upload count, for instance, fails the upload).
/// </summary>
internal sealed record ResumeRequest(
    string File,
    string App,
    string Repetition,
    string Suites,
    string Routes,
    string Concurrencies,
    string CableClients,
    string UploadRepetitions,
    string ValidateEvery,
    string LoadMax )
{
    /// <summary>The request from the command line, or null when it does not have every argument.</summary>
    public static ResumeRequest? Parse( string[] args )
    {
        if ( args.Length != 10 )
        {
            return null;
        }

        return new ResumeRequest( args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9] );
    }

    /// <summary>An Accept-Encoding: identity app (bench/m3's container suite) measures HTTP only.</summary>
    public bool IsIdentityRun => ResumeResult.Words( App.Replace( '-', ' ' ) ).Contains( "identity", StringComparer.Ordinal );

    /// <summary>A counters app also records an EventPipe trace beside its checkpoint.</summary>
    public bool IsCountersRun => App.EndsWith( "-counters", StringComparison.Ordinal );
}

internal static class ResumeResult
{
    private const string Usage = "resume-result: expected FILE APP REP SUITES ROUTES CONCS CABLE_CLIENTS UPLOAD_REPS VALIDATE_EVERY LOAD_MAX";

    /// <summary>The Cable verification counters that must all be zero.</summary>
    private static readonly string[] CableFailureCounters =
    [
        "disconnected",
        "duplicates",
        "mismatches",
        "missing",
        "off_stream",
        "post_failures",
        "unconfirmed",
        "unexpected",
    ];

    public static int Run( string[] args )
    {
        var request = ResumeRequest.Parse( args );
        if ( request is null )
        {
            Console.Error.WriteLine( Usage );
            return 2;
        }

        try
        {
            using var document = JsonDocument.Parse( File.ReadAllBytes( request.File ) );

            return Validate( document.RootElement, request );
        }
        catch ( Exception exception ) when ( exception is IOException
            or JsonException
            or InvalidDataException
            or FormatException
            or InvalidOperationException
            or UnauthorizedAccessException )
        {
            Console.Error.WriteLine( $"resume-result: {Path.GetFileName( request.File )}: {exception.Message}" );
            return 1;
        }
    }

    internal static string[] Words( string value ) => value.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

    /// <summary>
    /// Checks the checkpoint against the request: 0 when it is complete and valid, 3 when only its upload results must be
    /// measured again. Throws <see cref="InvalidDataException"/> for anything else.
    /// </summary>
    private static int Validate( JsonElement root, ResumeRequest request )
    {
        var suites = Words( request.Suites ).ToHashSet( StringComparer.Ordinal );
        var identity = request.IsIdentityRun;
        Require(
            Text( root, "app" ) == request.App && Number( root, "rep" ) == Parse( request.Repetition ),
            "app or repetition does not match its checkpoint" );
        var load = ParseStartLoad( Text( root, "loadavg_start" ) );
        Require( load >= 0 && load < Parse( request.LoadMax ), "run did not start below the required host load" );

        string[] routes = suites.Contains( "http" ) ? Words( request.Routes ) : [];
        CheckHttp( root, routes, Words( request.Concurrencies ), Parse( request.ValidateEvery ) );
        string[] cableClients = suites.Contains( "cable" ) && !identity ? Words( request.CableClients ) : [];
        CheckCable( root, cableClients );

        try
        {
            CheckUpload( root, suites.Contains( "upload" ) && !identity, Parse( request.UploadRepetitions ) );
        }
        catch ( InvalidDataException exception )
        {
            Console.Error.WriteLine( $"resume-result: {request.App} repetition {request.Repetition}: upload must be repeated: {exception.Message}" );
            return 3;
        }

        if ( request.IsCountersRun )
        {
            var checkpointDirectory = Path.GetDirectoryName( Path.GetFullPath( request.File ) )!;
            var trace = Path.Combine( checkpointDirectory, "raw", $"{request.App}-{request.Repetition}", "campfire.nettrace" );
            Require( File.Exists( trace ) && new FileInfo( trace ).Length > 0, "counter checkpoint has no trace" );
        }

        Console.WriteLine( $"resume-result: {request.App} repetition {request.Repetition} is complete and valid" );
        return 0;
    }

    /// <summary>The 1-minute load average at the run's start: the first field of its recorded /proc/loadavg line.</summary>
    private static double ParseStartLoad( string loadAverages )
    {
        var fields = Words( loadAverages );
        Require( fields.Length > 0, "loadavg_start is empty" );

        return Parse( fields[0] );
    }

    private static double Parse( string value )
    {
        var parsed = double.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number );
        Require( parsed && double.IsFinite( number ), "expected a finite number" );

        return number;
    }

    private static JsonElement Property( JsonElement value, string name )
    {
        Require( value.TryReadProperty( name, out var property ), $"missing {name}" );

        return property;
    }

    private static string Text( JsonElement value, string name )
    {
        Require( Property( value, name ).TryReadString( out var text ), $"{name} must be a string" );

        return text;
    }

    private static double Number( JsonElement value, string name )
    {
        Require( Property( value, name ).TryReadFiniteDouble( out var number ), $"{name} must be a finite number" );

        return number;
    }

    private static JsonElement Array( JsonElement value, string name )
    {
        var property = Property( value, name );
        Require( property.ValueKind == JsonValueKind.Array, $"{name} must be an array" );

        return property;
    }

    /// <summary>One error-free, fully validated row per route and concurrency, and no other rows.</summary>
    private static void CheckHttp( JsonElement root, string[] routes, string[] concurrencies, double validateEvery )
    {
        var expected = routes
            .SelectMany( route => concurrencies.Select( concurrency => (Route: route, Concurrency: Parse( concurrency )) ) )
            .ToHashSet( );
        foreach ( var row in Array( root, "http" ).EnumerateArray( ) )
        {
            var route = Text( row, "route" );
            Require( expected.Remove( (route, Number( row, "conc" )) ), "duplicate or unexpected HTTP row" );
            var count = Number( row, "ok" );
            Require(
                count > 0 && Number( row, "errors" ) == 0 && Number( row, "mismatches" ) == 0,
                "HTTP row has errors, mismatches or no responses" );
            Require( Number( row, "ended_unix_us" ) > Number( row, "started_unix_us" ), "invalid HTTP interval" );
            Require(
                Number( Property( row, "latency" ), "p95_ms" ) >= 0
                    && Number( Property( row, "server_cpu" ), "cpu_ms_per_response" ) >= 0,
                "invalid latency or CPU measurement" );
            var validation = Property( row, "validation" );
            Require( Number( validation, "checked" ) == count && Number( validation, "mismatches" ) == 0, "HTTP validation is incomplete" );
            var checkedBodies = Number( validation, "bodies_checked" );
            Require( BodiesValidated( route, checkedBodies, count, validateEvery ), "HTTP body validation is incomplete" );
        }

        Require( expected.Count == 0, "missing HTTP rows" );
    }

    /// <summary>
    /// Whether a row checked enough response bodies: never more than it received; for a route with a seed-row body marker
    /// at least one, and every one when bench/run validated every response (VALIDATE_EVERY=1).
    /// </summary>
    private static bool BodiesValidated( string route, double checkedBodies, double responses, double validateEvery )
    {
        if ( checkedBodies < 0 || checkedBodies > responses )
        {
            return false;
        }

        // The imported workload checks avatar/CSS status and encoding, without a seed-row body marker.
        if ( route is "avatar" or "static_css" )
        {
            return true;
        }

        return checkedBodies > 0 && (validateEvery != 1 || checkedBodies == responses);
    }

    /// <summary>One fully subscribed row per client count with every delivery verified, and no other rows.</summary>
    private static void CheckCable( JsonElement root, string[] clients )
    {
        var expected = clients.Select( Parse ).ToHashSet( );
        foreach ( var row in Array( root, "cable" ).EnumerateArray( ) )
        {
            var count = Number( row, "clients" );
            Require( expected.Remove( count ), "duplicate or unexpected Cable row" );
            Require( Number( row, "ready" ) == count && Number( row, "failed" ) == 0, "Cable subscriptions are incomplete" );
            var verification = Property( row, "verification" );
            foreach ( var name in CableFailureCounters )
            {
                Require( Number( verification, name ) == 0, $"Cable has {name}" );
            }

            var delivered = Number( verification, "delivered" );
            Require(
                delivered > 0 && delivered == Number( verification, "expected_deliveries" ),
                "Cable delivery validation is incomplete" );
            var throughput = Property( row, "throughput" );
            Require(
                Number( throughput, "complete" ) > 0 && Number( throughput, "complete" ) == Number( throughput, "posted" ),
                "Cable throughput delivery is incomplete" );
        }

        Require( expected.Count == 0, "missing Cable rows" );
    }

    /// <summary>Every upload repetition posted and served a raster thumbnail; no upload results when none were expected.</summary>
    private static void CheckUpload( JsonElement root, bool expected, double repetitions )
    {
        var upload = Property( root, "upload" );
        Require( upload.ValueKind == JsonValueKind.Object, "upload must be an object" );
        if ( !expected )
        {
            Require( !upload.EnumerateObject( ).Any( ), "unexpected upload results" );
            return;
        }

        Require( Number( upload, "failures" ) == 0, "upload failed" );
        var runs = Array( upload, "runs" );
        Require( runs.GetArrayLength( ) == repetitions, "missing upload repetitions" );
        foreach ( var row in runs.EnumerateArray( ) )
        {
            var postStatus = Number( row, "post_status" );
            Require(
                !string.IsNullOrEmpty( Text( row, "thumb_url" ) ) && Property( row, "thumb_raster" ).ValueKind == JsonValueKind.True,
                "attachment thumbnail was not verified as raster media" );
            Require(
                postStatus >= 200
                    && postStatus < 300
                    && Number( row, "thumb_status" ) == 200
                    && Number( row, "thumb_bytes" ) > 0
                    && Text( row, "thumb_type" ).StartsWith( "image/", StringComparison.Ordinal ),
                "invalid upload or thumbnail response" );
        }
    }

    private static void Require( [DoesNotReturnIf( false )] bool condition, string message )
    {
        if ( !condition )
        {
            throw new InvalidDataException( message );
        }
    }
}
