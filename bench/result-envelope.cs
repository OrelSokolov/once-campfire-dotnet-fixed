#!/usr/bin/env dotnet
// Assemble a repetition after measurement, including the observed generator logging setting.
// Usage: result-envelope.cs FILE APP REP COLD IDLE_CURRENT IDLE_ANON PEAK CG_PEAK HTTP CABLE UPLOAD LOAD_START LOAD_END EXTRAS
#:property UseSharedCompilation=false
#:property RestorePackagesWithLockFile=false

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

var envelope = EnvelopeArguments.Parse( args );
if ( envelope is null )
{
    Console.Error.WriteLine( "result-envelope: expected FILE APP REP COLD IDLE_CURRENT IDLE_ANON PEAK CG_PEAK HTTP CABLE UPLOAD LOAD_START LOAD_END EXTRAS" );
    return 2;
}

var setting = Environment.GetEnvironmentVariable( "LOADGEN_DEBUG" );
if ( !string.IsNullOrEmpty( setting ) )
{
    Console.Error.WriteLine( "result-envelope: debug logging is enabled; refusing a timing result" );
    return 2;
}

var memory = JsonNode.Parse( envelope.Memory )!.AsObject( );
memory["idle_current_mb"] = Integer( envelope.IdleCurrentMegabytes );
memory["idle_anon_mb"] = Integer( envelope.IdleAnonymousMegabytes );
memory["cgroup_peak_mb"] = Integer( envelope.CgroupPeakMegabytes );
var result = JsonNode.Parse( envelope.Extras )!.AsObject( );
var repeatVisit = Environment.GetEnvironmentVariable( "BENCH_LAST_ROOM_COOKIE" ) ?? "0";
if ( repeatVisit is not ("0" or "1") )
{
    Console.Error.WriteLine( "result-envelope: BENCH_LAST_ROOM_COOKIE must be 0 or 1" );
    return 2;
}

result["repeat_visit_cookie"] = repeatVisit == "1";
result["app"] = envelope.App;
result["rep"] = Integer( envelope.Repetition );
result["cold_start_ms"] = Integer( envelope.ColdStartMilliseconds );
result["memory"] = memory;
result["http"] = JsonNode.Parse( envelope.Http );
result["cable"] = JsonNode.Parse( envelope.Cable );
result["upload"] = JsonNode.Parse( envelope.Upload );
result["loadavg_start"] = envelope.LoadStart;
result["loadavg_end"] = envelope.LoadEnd;
result["loadgen_debug"] = new JsonObject
{
    ["enabled"] = false,
    ["environment"] = setting is null ? "unset" : "empty",
};

// Every generator run (HTTP rows, Cable rows, the upload when it ran) must report its debug logging off.
var generatorRuns = result["http"]!.AsArray( ).Concat( result["cable"]!.AsArray( ) ).ToList( );
if ( result["upload"]!.AsObject( ).Count > 0 )
{
    generatorRuns.Add( result["upload"] );
}

foreach ( var run in generatorRuns )
{
    if ( run?["loadgen"]?["debug_logging"]?.GetValue<bool>( ) != false )
    {
        Console.Error.WriteLine( "result-envelope: generator did not confirm debug logging is off" );
        return 1;
    }
}

File.WriteAllText( envelope.File, result.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) + "\n" );
return 0;

static long Integer( string text ) => long.Parse( text, CultureInfo.InvariantCulture );

/// <summary>
/// bench/run's measurements of one repetition, as text: whole numbers, JSON documents (memory, the HTTP and Cable rows,
/// the upload, and the extra fields the result starts from), and /proc/loadavg lines.
/// </summary>
internal sealed record EnvelopeArguments(
    string File,
    string App,
    string Repetition,
    string ColdStartMilliseconds,
    string IdleCurrentMegabytes,
    string IdleAnonymousMegabytes,
    string Memory,
    string CgroupPeakMegabytes,
    string Http,
    string Cable,
    string Upload,
    string LoadStart,
    string LoadEnd,
    string Extras )
{
    /// <summary>The arguments in their usage order, or null when there are not exactly fourteen.</summary>
    public static EnvelopeArguments? Parse( string[] args )
    {
        if ( args.Length != 14 )
        {
            return null;
        }

        return new EnvelopeArguments(
            args[0],
            args[1],
            args[2],
            args[3],
            args[4],
            args[5],
            args[6],
            args[7],
            args[8],
            args[9],
            args[10],
            args[11],
            args[12],
            args[13] );
    }
}
