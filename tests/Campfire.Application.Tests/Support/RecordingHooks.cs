using Campfire.Application.Data.Hooks;

namespace Campfire.Application.Tests.Support;

/// <summary>
/// Every hook, recording each call as a line in order (<c>assert_enqueued_jobs</c> and friends). <see cref="Intercept"/>
/// runs first for each call and may throw, to fail a step on purpose.
/// </summary>
internal sealed class RecordingHooks :
    IAttachmentUploads, IStorageJobs, IPushJobs, IWebhookJobs, IBannedContentJobs, IRemoteConnections
{
    private readonly Lock _gate = new( );

    private readonly List<string> _events = [];

    /// <summary>Called with each event before it is recorded.</summary>
    public Action<string>? Intercept { get; set; }

    public IReadOnlyList<string> Events
    {
        get
        {
            lock ( _gate )
            {
                return [.. _events];
            }
        }
    }

    public CampfireHooks ToHooks( IMessageBodyText bodyText ) => new( bodyText, this, this, this, this, this, this );

    public void Clear( )
    {
        lock ( _gate )
        {
            _events.Clear( );
        }
    }

    public void Upload( PendingUpload upload ) => Record( $"upload blob {upload.BlobId} from {upload.Source}" );

    public void AnalyzeLater( long blobId ) => Record( $"analyze blob {blobId}" );

    public void PurgeLater( long blobId ) => Record( $"purge blob {blobId}" );

    public void PushMessageLater( long roomId, long messageId ) => Record( $"push room {roomId} message {messageId}" );

    public void DeliverLater( long botId, long messageId ) => Record( $"webhook bot {botId} message {messageId}" );

    public void RemoveLater( long userId ) => Record( $"remove banned content of user {userId}" );

    public void Disconnect( long userId, bool reconnect ) => Record( $"disconnect user {userId} reconnect={reconnect.ToString( ).ToLowerInvariant( )}" );

    private void Record( string line )
    {
        Intercept?.Invoke( line );
        lock ( _gate )
        {
            _events.Add( line );
        }
    }
}
