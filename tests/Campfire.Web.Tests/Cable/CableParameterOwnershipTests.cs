using System.Text.Json;
using Campfire.Web.Cable;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Campfire.Web.Tests.Cable;

public sealed class CableParameterOwnershipTests
{
    [Fact]
    public async Task A_confirmed_subscription_keeps_plain_values_without_a_live_json_document( )
    {
        var observed = new TaskCompletionSource<object>( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var app = await CableTestHost.StartAsync( ( services, cable ) =>
        {
            services.AddSingleton<ICableConnectionAuthenticator, CookieAuthenticator>( );
            cable.AddChannel( "OwnedParams", _ => new InspectChannel( observed ) );
        } );
        await using var client = await app.ConnectAsync( "session_token=1" );
        Assert.Equal( CableProtocol.Welcome( ), await client.NextTextAsync( ) );
        const string Identifier = "{\"channel\":\"OwnedParams\",\"room_id\":12,\"text\":\"hello 👍\",\"extra\":{\"flags\":[true,null,1e3,-5.9]}}";
        await client.SubscribeAsync( Identifier );
        using var confirmation = JsonDocument.Parse( await client.NextTextAsync( ) );
        Assert.Equal( "confirm_subscription", confirmation.RootElement.GetProperty( "type" ).GetString( ) );
        Assert.Equal( Identifier, confirmation.RootElement.GetProperty( "identifier" ).GetString( ) );

        var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>( await observed.Task.WaitAsync(
            TimeSpan.FromSeconds( 10 ),
            TestContext.Current.CancellationToken ) );
        Assert.Equal( "OwnedParams", Assert.IsType<string>( parameters["channel"] ) );
        Assert.Equal( 12, Assert.IsType<long>( parameters["room_id"] ) );
        Assert.Equal( "hello 👍", Assert.IsType<string>( parameters["text"] ) );
        var extra = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>( parameters["extra"] );
        var flags = Assert.IsType<object?[]>( extra["flags"] );
        Assert.Equal( 4, flags.Length );
        Assert.True( Assert.IsType<bool>( flags[0] ) );
        Assert.Null( flags[1] );
        Assert.Equal( 1000d, Assert.IsType<double>( flags[2] ) );
        Assert.Equal( -5.9d, Assert.IsType<double>( flags[3] ) );
    }

    private sealed class InspectChannel( TaskCompletionSource<object> observed ) : CableChannel
    {
        public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
        {
            observed.TrySetResult( Params );
            return ValueTask.CompletedTask;
        }
    }
}
