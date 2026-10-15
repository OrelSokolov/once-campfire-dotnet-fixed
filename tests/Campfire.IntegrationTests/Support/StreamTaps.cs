using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Campfire.Web.Channels;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// Taps on named pub/sub streams: each broadcast is recorded in <see cref="SliceEvents"/> as
/// <c>stream &lt;label&gt; &lt;action&gt; &lt;target&gt;</c>, and its payload kept for assertions.
/// </summary>
public sealed partial class StreamTaps : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<(string Label, string Payload)> _payloads = [];
    private readonly Lock _gate = new( );

    private StreamTaps( )
    {
    }

    /// <summary>Every turbo stream received (the payload's JSON string, decoded), with its stream's label, in order.</summary>
    public IReadOnlyList<(string Label, string Payload)> Payloads
    {
        get
        {
            lock ( _gate )
            {
                return [.. _payloads];
            }
        }
    }

    /// <summary>
    /// Taps each stream: <c>("rooms", ["rooms"])</c> is the global <c>:rooms</c> stream,
    /// <c>("kevin", RoomBroadcasts.UserRooms(id))</c> a user's <c>[ user, :rooms ]</c>.
    /// </summary>
    public static StreamTaps On( SliceAppFactory factory, params (string Label, string[] Streamables)[] streams )
    {
        ArgumentNullException.ThrowIfNull( factory );
        ArgumentNullException.ThrowIfNull( streams );

        var app = factory.App;
        var taps = new StreamTaps( );
        foreach ( var (label, streamables) in streams )
        {
            taps._subscriptions.Add( app.Cable.Hub.Subscribe( string.Join( ':', streamables ), payload =>
            {
                lock ( taps._gate )
                {
                    taps._payloads.Add( (label, JsonNode.Parse( payload )?.GetValue<string>( ) ?? "") );
                }

                factory.Events.Record( $"stream {label} {Describe( payload )}" );
            } ) );
        }

        return taps;
    }

    /// <summary>The global <c>:rooms</c> stream.</summary>
    public static (string, string[]) Rooms => ("rooms", [RoomBroadcasts.RoomsStream]);

    /// <summary>A user's <c>[ user, :rooms ]</c>, labelled with the fixture's name.</summary>
    public static (string, string[]) UserRooms( string user ) => (user, RoomBroadcasts.UserRooms( SliceAppFactory.Id( user ) ));

    public void Dispose( ) => _subscriptions.ForEach( subscription => subscription.Dispose( ) );

    /// <summary>A turbo stream's action and target (<c>append messages_rooms_closed_1</c>).</summary>
    private static string Describe( string payload )
    {
        var action = ActionPattern( ).Match( payload );
        var target = TargetPattern( ).Match( payload );
        return action.Success ? $"{action.Groups[1].Value} {(target.Success ? target.Groups[1].Value : "")}".TrimEnd( ) : payload;
    }

    [GeneratedRegex( @"action=\\""([^\\]*)" )]
    private static partial Regex ActionPattern( );

    [GeneratedRegex( @"target=\\""([^\\]*)" )]
    private static partial Regex TargetPattern( );
}
