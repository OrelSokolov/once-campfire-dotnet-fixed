#!/usr/bin/env dotnet
// Parse one benchmark EventPipe trace and its run JSON. Allocation ticks are sampled estimates,
// attributed only to recorded HTTP intervals; idle, warm-up, cable and upload allocations are separate.
// Usage: dotnet bench/trace-report.cs TRACE RUN_JSON OUTPUT_JSON
#:package Microsoft.Diagnostics.Tracing.TraceEvent
#:property RestorePackagesWithLockFile=true
#:property NuGetLockFilePath=trace-report.cs.lock.json

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Tracing;

if ( args.Length != 3 )
{
    Console.Error.WriteLine( "Usage: trace-report.cs TRACE RUN_JSON OUTPUT_JSON" );
    return 2;
}

using var run = JsonDocument.Parse( await File.ReadAllTextAsync( args[1] ) );
var windows = run.RootElement.GetProperty( "http" ).EnumerateArray( ).Select( item => new AllocationWindow(
    item.GetProperty( "route" ).GetString( )!, item.GetProperty( "conc" ).GetInt32( ),
    item.GetProperty( "started_unix_us" ).GetInt64( ), item.GetProperty( "ended_unix_us" ).GetInt64( ),
    item.GetProperty( "latency" ).GetProperty( "n" ).GetInt64( ) ) ).ToArray( );
if ( windows.Any( item => item.Ended <= item.Started || item.Responses <= 0 ) )
{
    throw new InvalidDataException( "Measured HTTP intervals must have increasing UTC boundaries and completed responses." );
}

var ordered = windows.OrderBy( item => item.Started ).ToArray( );
for ( var index = 1; index < ordered.Length; index++ )
{
    if ( ordered[index].Started < ordered[index - 1].Ended )
    {
        throw new InvalidDataException( "Measured HTTP intervals overlap; allocation attribution is ambiguous." );
    }
}

var heaps = new SortedDictionary<int, long>( );
var allocationTypes = new Dictionary<string, long>( StringComparer.Ordinal );
long allocationEvents = 0;
long allocationBytes = 0;
long outsideHttpBytes = 0;
long heapEvents = 0;
using var source = new EventPipeEventSource( args[0] );
source.Clr.GCAllocationTick += data =>
{
    var bytes = data.AllocationAmount64 > 0 ? data.AllocationAmount64 : data.AllocationAmount;
    allocationEvents++;
    allocationBytes += bytes;
    var name = data.TypeName ?? "(unknown)";
    allocationTypes[name] = allocationTypes.GetValueOrDefault( name ) + bytes;
    var unixMicroseconds = (data.TimeStamp.ToUniversalTime( ).Ticks - DateTime.UnixEpoch.Ticks) / 10;
    var window = windows.FirstOrDefault( item => unixMicroseconds >= item.Started && unixMicroseconds < item.Ended );
    if ( window is null )
    {
        outsideHttpBytes += bytes;
    }
    else
    {
        window.Add( bytes );
    }
};
source.Clr.GCGlobalHeapHistory += data =>
{
    heapEvents++;
    heaps[data.NumHeaps] = heaps.GetValueOrDefault( data.NumHeaps ) + 1;
};
source.Process( );

var app = run.RootElement.GetProperty( "app" ).GetString( );
var repetition = run.RootElement.GetProperty( "rep" ).GetInt32( );
var heapCounts = new JsonObject( );
foreach ( var (count, events) in heaps )
{
    heapCounts[count.ToString( CultureInfo.InvariantCulture )] = events;
}

var result = new JsonObject
{
    ["app"] = app,
    ["rep"] = repetition,
    ["trace"] = Path.GetFullPath( args[0] ),
    ["parser"] = "Microsoft.Diagnostics.Tracing.TraceEvent 3.2.5",
    ["events_lost"] = source.EventsLost,
    ["allocation_events"] = allocationEvents,
    ["allocation_bytes_estimate"] = allocationBytes,
    ["outside_measured_http_bytes_estimate"] = outsideHttpBytes,
    ["heap_history_events"] = heapEvents,
    ["effective_heap_counts"] = heapCounts,
    ["http"] = new JsonArray( windows.Select( item => ( JsonNode? ) new JsonObject
    {
        ["route"] = item.Route,
        ["conc"] = item.Concurrency,
        ["responses"] = item.Responses,
        ["allocation_tick_events"] = item.Events,
        ["allocation_bytes_estimate"] = item.Bytes,
        ["allocated_bytes_per_response_estimate"] = item.Responses > 0 ? ( double? ) item.Bytes / item.Responses : null
    } ).ToArray( ) ),
    ["top_sampled_allocation_types"] = new JsonArray( allocationTypes.OrderByDescending( item => item.Value ).Take( 20 )
        .Select( item => ( JsonNode? ) new JsonObject { ["type"] = item.Key, ["sampled_bytes"] = item.Value } ).ToArray( ) ),
    ["caveat"] = "AllocationTick is sampled, not an exact allocation census. "
        + "Type names identify the object at a tick, not every byte attributed to that type. "
        + "HTTP boundaries use real UTC timestamps. "
        + "Events lost or missing allocation/heap events invalidate attribution; SQL wall time is not instrumented.",
};
await File.WriteAllTextAsync( args[2], result.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
Console.WriteLine( $"{app} rep {repetition}: {allocationEvents} allocation ticks, {heapEvents} heap histories, {source.EventsLost} lost events; {args[2]}" );
return allocationEvents > 0 && heapEvents > 0 && source.EventsLost == 0 ? 0 : 1;

/// <summary>One measured HTTP run's interval and the allocation ticks sampled inside it.</summary>
internal sealed class AllocationWindow( string route, int concurrency, long started, long ended, long responses )
{
    public string Route { get; } = route;

    public int Concurrency { get; } = concurrency;

    public long Started { get; } = started;

    public long Ended { get; } = ended;

    public long Responses { get; } = responses;

    public long Events { get; private set; }

    public long Bytes { get; private set; }

    public void Add( long bytes )
    {
        Events++;
        Bytes += bytes;
    }
}
