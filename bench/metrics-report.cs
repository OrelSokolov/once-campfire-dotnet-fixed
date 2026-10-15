#!/usr/bin/env dotnet
// Summarize the HTTP p95 latency and whole-container CPU measurements from one bench/run output directory.
// Usage: dotnet bench/metrics-report.cs SUITE_OUTPUT_DIR
// Exit codes: 0 printed, 1 a run file is unreadable or malformed, 2 bad usage.
#:property RestorePackagesWithLockFile=false
#:include ../eng/lib/JsonElementExtensions.cs

using System.Globalization;
using System.Text;
using System.Text.Json;

return MetricsReport.Run( args );

internal static class MetricsReport
{
    private const string CpuNote = "Server CPU is whole-container user + system CPU per completed HTTP response. "
        + "Sampling is outside the measured workload; Linux CPU accounting has 10 ms kernel-tick granularity, "
        + "and a worker replacement invalidates the observation.";

    /// <summary>The apps' column order: these first, then each one's variants, then the rest as found.</summary>
    private static readonly string[] PreferredApps = ["reference", "rust", "rust-v0.1.1", "dotnet-aot", "dotnet-jit"];

    public static int Run( string[] args )
    {
        if ( args.Length != 1 || !Directory.Exists( args[0] ) )
        {
            Console.Error.WriteLine( "Usage: metrics-report.cs SUITE_OUTPUT_DIR" );
            return 2;
        }

        var measurements = Load( Path.GetFullPath( args[0] ) );
        if ( measurements is null )
        {
            return 1;
        }

        Console.WriteLine( RenderReport( OrderApps( measurements.Apps ), measurements.Values ) );
        return 0;
    }

    /// <summary>Every run file's measurements, or null (after saying which file is bad) when one cannot be read.</summary>
    private static Measurements? Load( string outputDirectory )
    {
        var measurements = new Measurements( );
        var runFiles = Directory.EnumerateFiles( outputDirectory, "*-*.json", SearchOption.TopDirectoryOnly )
            .OrderBy( file => Path.GetFileName( file ), StringComparer.Ordinal );
        foreach ( var path in runFiles )
        {
            try
            {
                ReadRunFile( path, measurements );
            }
            catch ( Exception exception ) when ( exception is IOException
                or JsonException
                or InvalidDataException
                or UnauthorizedAccessException )
            {
                Console.Error.WriteLine( $"metrics-report: {Path.GetFileName( path )}: {exception.Message}" );
                return null;
            }
        }

        return measurements;
    }

    /// <summary>Adds one repetition's HTTP rows (one bench/run result file) to <paramref name="measurements"/>.</summary>
    private static void ReadRunFile( string path, Measurements measurements )
    {
        using var document = JsonDocument.Parse( File.ReadAllBytes( path ) );
        var (app, http) = ReadRoot( document.RootElement );
        measurements.AddApp( app );

        var index = 0;
        foreach ( var row in http.EnumerateArray( ) )
        {
            index++;
            var context = $"{Path.GetFileName( path )} HTTP row {index}";
            var (route, concurrency) = ReadKey( row, context );
            var p95 = ReadFiniteNumber( row, "latency", "p95_ms", context );
            var cpu = ReadFiniteNumber( row, "server_cpu", "cpu_ms_per_response", context );
            if ( cpu < 0 || p95 < 0 )
            {
                throw new InvalidDataException( $"{context}: CPU and latency must be nonnegative" );
            }

            measurements.AddMeasurement( new MetricKey( app, route, concurrency ), p95, cpu );
        }
    }

    /// <summary>The result's app and its HTTP rows.</summary>
    private static (string App, JsonElement Http) ReadRoot( JsonElement root )
    {
        if ( !root.TryReadProperty( "app", out var appElement )
            || !appElement.TryReadString( out var app )
            || !root.TryReadProperty( "http", out var http )
            || http.ValueKind != JsonValueKind.Array )
        {
            throw new InvalidDataException( "expected a string 'app' and an array 'http' at the JSON root" );
        }

        if ( string.IsNullOrWhiteSpace( app ) )
        {
            throw new InvalidDataException( "root 'app' must be a non-empty string" );
        }

        return (app, http);
    }

    private static string[] OrderApps( IReadOnlyList<string> apps ) =>
        apps.Select( ( app, index ) => (App: app, Index: index, Rank: AppRank( app )) )
            .OrderBy( item => item.Rank )
            .ThenBy( item => item.Index )
            .Select( item => item.App )
            .ToArray( );

    private static int AppRank( string app )
    {
        var exact = Array.IndexOf( PreferredApps, app );
        if ( exact >= 0 )
        {
            return exact;
        }

        for ( var index = PreferredApps.Length - 1; index >= 0; index-- )
        {
            if ( app.StartsWith( $"{PreferredApps[index]}-", StringComparison.Ordinal ) )
            {
                return PreferredApps.Length + index;
            }
        }

        return PreferredApps.Length * 2;
    }

    private static (string Route, int Concurrency) ReadKey( JsonElement row, string context )
    {
        if ( !row.TryReadProperty( "route", out var routeElement )
            || !routeElement.TryReadString( out var route )
            || !row.TryReadProperty( "conc", out var concurrencyElement )
            || !concurrencyElement.TryReadInt32( out var concurrency )
            || concurrency <= 0 )
        {
            throw new InvalidDataException( $"{context}: expected string route and integer conc" );
        }

        if ( string.IsNullOrWhiteSpace( route ) )
        {
            throw new InvalidDataException( $"{context}: route must be a non-empty string" );
        }

        return (route, concurrency);
    }

    private static double ReadFiniteNumber( JsonElement row, string groupName, string metricName, string context )
    {
        if ( !row.TryReadProperty( groupName, out var group )
            || !group.TryReadProperty( metricName, out var metric )
            || !metric.TryReadFiniteDouble( out var value ) )
        {
            throw new InvalidDataException( $"{context}: expected finite numeric {groupName}.{metricName}" );
        }

        return value;
    }

    private static string RenderReport( IReadOnlyList<string> apps, IReadOnlyDictionary<MetricKey, MetricValues> measurements )
    {
        var report = new StringBuilder( );
        report.AppendLine( "### HTTP p95 and server CPU" );
        report.AppendLine( );
        report.AppendLine( CpuNote );
        if ( measurements.Count == 0 )
        {
            report.AppendLine( );
            report.AppendLine( "No HTTP metrics were recorded." );
            return report.ToString( ).TrimEnd( );
        }

        var groups = measurements.Keys
            .Select( key => (key.Route, key.Concurrency) )
            .Distinct( )
            .OrderBy( key => key.Route, StringComparer.Ordinal )
            .ThenBy( key => key.Concurrency );
        foreach ( var (route, concurrency) in groups )
        {
            var groupApps = apps.Where( app => measurements.ContainsKey( new MetricKey( app, route, concurrency ) ) ).ToArray( );
            report.AppendLine( );
            report.AppendLine( $"#### `{Escape( route )}` — concurrency {concurrency}" );
            report.AppendLine( );
            report.Append( "| Metric |" );
            foreach ( var app in groupApps )
            {
                report.Append( $" {Escape( app )} |" );
            }

            report.AppendLine( );
            report.Append( "|---|" );
            report.Append( string.Concat( Enumerable.Repeat( "---|", groupApps.Length ) ) );
            report.AppendLine( );
            AppendMetricRow(
                report,
                "p95 latency (ms)",
                groupApps,
                app => measurements[new MetricKey( app, route, concurrency )].Latencies );
            AppendMetricRow(
                report,
                "whole-container CPU (ms/response)",
                groupApps,
                app => measurements[new MetricKey( app, route, concurrency )].CpuPerResponse );
        }

        return report.ToString( ).TrimEnd( );
    }

    private static void AppendMetricRow( StringBuilder report, string label, IReadOnlyList<string> apps, Func<string, List<double>> valuesFor )
    {
        report.Append( $"| {label} |" );
        foreach ( var app in apps )
        {
            var values = valuesFor( app ).Order( ).ToArray( );
            var median = Median( values );
            report.Append( $" {Format( median )} [{Format( values[0] )}–{Format( values[^1] )}] (n={values.Length}) |" );
        }

        report.AppendLine( );
    }

    private static double Median( double[] values )
    {
        var middle = values.Length / 2;
        if ( values.Length % 2 == 1 )
        {
            return values[middle];
        }

        return (values[middle - 1] + values[middle]) / 2;
    }

    private static string Format( double value ) => value.ToString( "F3", CultureInfo.InvariantCulture );

    private static string Escape( string value ) => value.Replace( "|", "\\|", StringComparison.Ordinal );
}

/// <summary>The measurements read so far: the apps in the order first seen, and each app/route/concurrency's values.</summary>
internal sealed class Measurements
{
    private readonly List<string> _apps = [];
    private readonly Dictionary<MetricKey, MetricValues> _values = new( );

    public IReadOnlyList<string> Apps => _apps;

    public IReadOnlyDictionary<MetricKey, MetricValues> Values => _values;

    public void AddApp( string app )
    {
        if ( !_apps.Contains( app, StringComparer.Ordinal ) )
        {
            _apps.Add( app );
        }
    }

    /// <summary>Adds one repetition's p95 latency and CPU per response for <paramref name="key"/>.</summary>
    public void AddMeasurement( MetricKey key, double p95, double cpuPerResponse )
    {
        if ( !_values.TryGetValue( key, out var values ) )
        {
            values = new MetricValues( );
            _values.Add( key, values );
        }

        values.Latencies.Add( p95 );
        values.CpuPerResponse.Add( cpuPerResponse );
    }
}

internal sealed record MetricKey( string App, string Route, int Concurrency );

internal sealed class MetricValues
{
    public List<double> Latencies { get; } = [];

    public List<double> CpuPerResponse { get; } = [];
}
