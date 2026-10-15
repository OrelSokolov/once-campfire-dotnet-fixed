using Campfire.IntegrationTests.Support;
using Campfire.Web.Cable;
using Campfire.Web.Channels;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Campfire.IntegrationTests.Channels;

public sealed class ProductionCableAdmissionTests
{
    [Theory]
    [InlineData( "session", false )]
    [InlineData( "ban", false )]
    [InlineData( "deactivate", false )]
    [InlineData( "session", true )]
    [InlineData( "ban", true )]
    [InlineData( "deactivate", true )]
    public async Task Revocation_of_authenticated_authority_in_either_admission_window_sends_no_welcome( string revocation, bool afterSubscription )
    {
        await using var factory = new AdmissionFactory( );
        factory.Barrier.AfterSubscription = afterSubscription;
        using var client = factory.Client( );
        var cableServer = factory.Services.GetRequiredService<CableServer>( );
        var user = SliceAppFactory.Id( "david" );
        client.SignInAs( factory, user );
        var connecting = client.ConnectCableAsync( );
        try
        {
            var identity = await factory.Barrier.FirstLookup.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), SliceClient.Cancellation );
            Assert.NotNull( identity );
            Assert.Equal( user, Assert.IsType<CableUser>( identity.CurrentUser ).Id );
            if ( afterSubscription )
            {
                await factory.Barrier.BeforeWelcome.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), SliceClient.Cancellation );
                Assert.Equal( 1, cableServer.Hub.GroupCount( CableServer.InternalChannel( identity.ConnectionIdentifier ) ) );
            }

            factory.Execute( revocation switch
            {
                "session" => $"DELETE FROM sessions WHERE user_id={user}",
                "ban" => $"UPDATE users SET status=2 WHERE id={user}",
                _ => $"UPDATE users SET status=1 WHERE id={user}"
            } );
        }
        finally
        {
            factory.Barrier.Continue.TrySetResult( );
        }

        await using var cable = await connecting;
        Assert.Equal( Realtime.Unauthorized, await cable.NextTextAsync( ) );
        await SliceWait.UntilAsync( ( ) => cableServer.ConnectionCount == 0 );
        Assert.Equal( 0, cableServer.ConnectionCount );
    }

    [Fact]
    public async Task An_authenticated_control_with_no_revocation_gets_a_welcome( )
    {
        await using var factory = new AdmissionFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        factory.Barrier.Continue.TrySetResult( );
        await using var cable = await client.ConnectCableAsync( );
        Assert.NotNull( await factory.Barrier.FirstLookup.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), SliceClient.Cancellation ) );
        Assert.Equal( CableProtocol.Welcome( ), await cable.NextTextAsync( ) );
    }

    [Fact]
    public async Task A_welcomed_connection_does_not_keep_browser_authentication_results_or_principals( )
    {
        await using var factory = new AdmissionFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        factory.Barrier.Continue.TrySetResult( );
        await using var cable = await Realtime.ConnectAsync( client );
        var request = Assert.IsAssignableFrom<HttpContext>( factory.Barrier.Request );
        Assert.Null( request.Features.Get<IAuthenticateResultFeature>( ) );
        Assert.Null( request.Features.Get<AuthenticatedSession>( ) );
        Assert.False( request.User.Identity?.IsAuthenticated ?? false );
    }

    [Theory]
    [InlineData( "expired" )]
    [InlineData( "tampered" )]
    [InlineData( "changed-token" )]
    public async Task Direct_cookie_admission_rejects_invalid_credentials( string failure )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var user = SliceAppFactory.Id( "david" );
        client.SignInAs( factory, user, failure == "expired" ? DateTimeOffset.UtcNow.AddDays( -1 ) : null );
        if ( failure == "tampered" )
        {
            client.AddCookie( BrowserAuthentication.CookieName, "not-a-protected-ticket" );
        }
        else if ( failure == "changed-token" )
        {
            factory.Execute( $"UPDATE sessions SET token='replacement-' || id WHERE user_id={user}" );
        }

        await using var cable = await client.ConnectCableAsync( );
        Assert.Equal( Realtime.Unauthorized, await cable.NextTextAsync( ) );
    }

    [Fact]
    public async Task Direct_cookie_admission_reassembles_a_chunked_browser_ticket( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        var value = client.CookieHeader.Split( '=', 2 )[1];
        var half = value.Length / 2;
        client.AddCookie( BrowserAuthentication.CookieName, "chunks-2" );
        client.AddCookie( BrowserAuthentication.CookieName + "C1", value[..half] );
        client.AddCookie( BrowserAuthentication.CookieName + "C2", value[half..] );
        await using var cable = await Realtime.ConnectAsync( client );
    }

    [Fact]
    public async Task Anonymous_upgrade_is_admitted_to_the_socket_only_to_send_reconnect_false( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        await using var cable = await client.ConnectCableAsync( );
        Assert.Equal( Realtime.Unauthorized, await cable.NextTextAsync( ) );
    }

    private sealed class AdmissionFactory : SliceAppFactory
    {
        public AdmissionBarrier Barrier { get; } = new( );

        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.ConfigureTestServices( services => services.AddSingleton<ICableConnectionAuthenticator>( provider =>
                new AdmissionAuthenticator( provider.GetRequiredService<SessionConnectionAuthenticator>( ), Barrier ) ) );
        }
    }

    private sealed class AdmissionBarrier
    {
        public bool AfterSubscription { get; set; }
        public TaskCompletionSource<CableIdentity?> FirstLookup { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource BeforeWelcome { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource Continue { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public HttpContext? Request { get; set; }
    }

    /// <summary>Only scheduling is intercepted; both database decisions use the production adapter.</summary>
    private sealed class AdmissionAuthenticator( SessionConnectionAuthenticator production, AdmissionBarrier barrier ) : ICableConnectionAuthenticator
    {
        public async ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken )
        {
            barrier.Request = httpContext;
            var identity = await production.ConnectAsync( httpContext, cancellationToken );
            barrier.FirstLookup.TrySetResult( identity );
            if ( !barrier.AfterSubscription )
            {
                await barrier.Continue.Task.WaitAsync( cancellationToken );
            }

            return identity;
        }

        public async ValueTask<bool> IsStillAuthorizedAsync( HttpContext httpContext, CableIdentity identity, CancellationToken cancellationToken )
        {
            barrier.BeforeWelcome.TrySetResult( );
            if ( barrier.AfterSubscription )
            {
                await barrier.Continue.Task.WaitAsync( cancellationToken );
            }

            return await production.IsStillAuthorizedAsync( httpContext, identity, cancellationToken );
        }
    }
}
