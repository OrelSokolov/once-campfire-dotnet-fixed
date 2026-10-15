using System.Diagnostics.Metrics;

namespace Campfire.Web.Http;

/// <summary>Route-pattern labels have a hard ceiling; user-supplied URLs never become retained metric keys.</summary>
internal sealed class GzipRouteMetrics
{
    /// <summary>The label of responses without a route pattern, and of every route past <see cref="MaximumRoutes"/>.</summary>
    internal const string OtherRoute = "other";

    private const int MaximumRoutes = 128;
    private const string RouteTag = "route";
    private readonly Lock _gate = new( );
    private readonly Dictionary<string, Counts> _routes = new( StringComparer.Ordinal );
    private readonly Counts _other = new( );

    internal GzipRouteMetrics( Meter meter )
    {
        meter.CreateObservableGauge( "campfire.gzip.route.hits", ( ) => Observe( counts => Interlocked.Read( ref counts.Hits ) ) );
        meter.CreateObservableGauge( "campfire.gzip.route.admits", ( ) => Observe( counts => Interlocked.Read( ref counts.Admits ) ) );
        meter.CreateObservableGauge( "campfire.gzip.route.one_offs", ( ) => Observe( counts => Interlocked.Read( ref counts.OneOffs ) ) );
        meter.CreateObservableGauge( "campfire.gzip.route.guard_responses", ( ) => Observe( counts => Interlocked.Read( ref counts.Guards ) ) );
        meter.CreateObservableGauge( "campfire.gzip.route.member_one_offs", ( ) => Observe( counts => Interlocked.Read( ref counts.MemberOneOffs ) ) );
    }

    internal Counts For( string route )
    {
        lock ( _gate )
        {
            if ( _routes.TryGetValue( route, out var counts ) )
            {
                return counts;
            }

            if ( _routes.Count >= MaximumRoutes || route == OtherRoute )
            {
                return _other;
            }

            counts = new( );
            _routes.Add( route, counts );
            return counts;
        }
    }

    private Measurement<long>[] Observe( Func<Counts, long> value )
    {
        lock ( _gate )
        {
            return _routes.Select( pair => new Measurement<long>( value( pair.Value ), new KeyValuePair<string, object?>( RouteTag, pair.Key ) ) )
                .Append( new Measurement<long>( value( _other ), new KeyValuePair<string, object?>( RouteTag, OtherRoute ) ) ).ToArray( );
        }
    }

    internal sealed class Counts
    {
        internal long Hits;
        internal long Admits;
        internal long OneOffs;
        internal long Guards;
        internal long MemberOneOffs;

        internal void Add( GzipAdmission admission )
        {
            switch ( admission )
            {
                case GzipAdmission.Hit:
                    Interlocked.Increment( ref Hits );
                    break;
                case GzipAdmission.Admit:
                    Interlocked.Increment( ref Admits );
                    break;
                case GzipAdmission.OneOff:
                    Interlocked.Increment( ref OneOffs );
                    break;
                default:
                    throw new ArgumentOutOfRangeException( nameof( admission ) );
            }
        }
    }
}
