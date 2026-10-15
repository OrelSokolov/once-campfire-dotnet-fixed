using Campfire.Application.Data.Hooks;
using Campfire.Web.App;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// The app's own hook adapters, each wrapped to record its calls in <see cref="SliceEvents"/> (and to run an armed
/// <see cref="SliceFaults"/> fault) before doing its real work.
/// </summary>
internal static class SliceHooks
{
    public static CampfireHooks Create( IServiceProvider provider, SliceEvents events, SliceFaults faults )
    {
        var jobs = new RecordingJobs( provider.GetRequiredService<JobHooks>( ), events );
        return new CampfireHooks(
            new RecordingBodyText( provider.GetRequiredService<IMessageBodyText>( ), events ),
            new RecordingUploads( provider.GetRequiredService<StagedUploads>( ), events, faults ),
            jobs,
            jobs,
            jobs,
            jobs,
            new RecordingRemoteConnections( provider.GetRequiredService<CableRemoteConnections>( ), events, faults ) );
    }

    private sealed class RecordingBodyText( IMessageBodyText inner, SliceEvents events ) : IMessageBodyText
    {
        public string ToPlainText( string storedBody, Func<long, string?> userName )
        {
            events.Record( "plain_text" );
            return inner.ToPlainText( storedBody, userName );
        }

        public IReadOnlyList<long> MentionedUserIds( string storedBody ) => inner.MentionedUserIds( storedBody );
    }

    private sealed class RecordingUploads( IAttachmentUploads inner, SliceEvents events, SliceFaults faults ) : IAttachmentUploads
    {
        public void Upload( PendingUpload upload )
        {
            events.Record( $"upload blob {upload.BlobId}" );
            var fault = faults.DuringUpload;
            faults.DuringUpload = null;
            fault?.Invoke( );
            inner.Upload( upload );
        }
    }

    private sealed class RecordingJobs( JobHooks inner, SliceEvents events ) : IStorageJobs, IPushJobs, IWebhookJobs, IBannedContentJobs
    {
        public void AnalyzeLater( long blobId )
        {
            events.Record( $"analyze_job blob {blobId}" );
            inner.AnalyzeLater( blobId );
        }

        public void PurgeLater( long blobId )
        {
            events.Record( $"purge_job blob {blobId}" );
            inner.PurgeLater( blobId );
        }

        public void PushMessageLater( long roomId, long messageId )
        {
            events.Record( $"push_job room {roomId} message {messageId}" );
            inner.PushMessageLater( roomId, messageId );
        }

        public void DeliverLater( long botId, long messageId )
        {
            events.Record( $"webhook_job bot {botId} message {messageId}" );
            inner.DeliverLater( botId, messageId );
        }

        public void RemoveLater( long userId )
        {
            events.Record( $"banned_content_job user {userId}" );
            inner.RemoveLater( userId );
        }
    }

    private sealed class RecordingRemoteConnections( IRemoteConnections inner, SliceEvents events, SliceFaults faults ) : IRemoteConnections
    {
        public void Disconnect( long userId, bool reconnect )
        {
            events.Record( $"disconnect user {userId} reconnect={reconnect.ToString( ).ToLowerInvariant( )}" );
            inner.Disconnect( userId, reconnect );
            var fault = faults.AfterDisconnect;
            faults.AfterDisconnect = null;
            fault?.Invoke( userId, reconnect );
        }
    }
}
