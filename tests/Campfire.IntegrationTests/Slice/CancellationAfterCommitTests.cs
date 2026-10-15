using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// The third failure case: the client goes away after the commit (the connection is aborted inside the after-commit
/// work). A disconnected caller cancels nothing that follows the commit: every step still runs.
/// </summary>
public sealed class CancellationAfterCommitTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    [Fact]
    public async Task Every_step_after_the_commit_still_runs( )
    {
        var room = SliceAppFactory.Id( "bender_and_kevin" );
        using var client = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Room( room ) );
        using var taps = await SliceTaps.OnRoomAsync( factory, room );
        var http = factory.Services.GetRequiredService<IHttpContextAccessor>( );
        factory.Events.Clear( );
        factory.Faults.DuringUpload = ( ) => http.HttpContext!.Abort( );

        var aborted = await Record.ExceptionAsync( ( ) => client.PostMessageAsync( room, token, file: SliceFiles.Moon( ), clientMessageId: "caller-left" ) );
        await SliceWait.UntilAsync( ( ) => factory.Events.All.Any( line => line.StartsWith( "webhook_job", StringComparison.Ordinal ) ) );

        var message = SliceRows.Message( factory, "caller-left" );
        var blob = SliceRows.AttachedBlob( factory, message.Id );
        Assert.IsType<HttpRequestException>( aborted );
        Assert.True( File.Exists( SliceRows.BlobPath( factory, blob.Key ) ) );
        Assert.True( File.Exists( SliceRows.BlobPath( factory, SliceRows.VariantImage( factory, blob.Id ) ) ), "process_attachment ran" );
        Assert.Equal( message.CreatedAt, SliceRows.UnreadAt( factory, "bender_bender_and_kevin" ) );
        Assert.Equal(
            [
                $"upload blob {blob.Id}",
                $"push_job room {room} message {message.Id}",
                $"analyze_job blob {blob.Id}",
                "broadcast append",
            ],
            factory.Events.Only( "upload", "push_job", "analyze_job", "broadcast", "webhook_job" ).Take( 4 ) );
        Assert.Equal( $"webhook_job bot {SliceAppFactory.Id( "bender" )} message {message.Id}", factory.Events.Only( "webhook_job" ).Single( ) );
    }
}
