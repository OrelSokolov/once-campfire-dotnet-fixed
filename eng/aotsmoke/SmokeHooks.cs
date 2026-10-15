using Campfire.Application.Data.Hooks;
using Campfire.Application.RichText;

namespace Campfire.AotSmoke;

/// <summary>Every data-layer hook, recording each call, so the smoke can check what ran after commit.</summary>
internal sealed class SmokeHooks :
    IAttachmentUploads, IStorageJobs, IPushJobs, IWebhookJobs, IBannedContentJobs, IRemoteConnections
{
    private readonly List<string> _events = [];

    public IReadOnlyList<string> Events => _events;

    public CampfireHooks ToHooks( ) => new( new PipelineBodyText( ), this, this, this, this, this, this );

    public void Clear( ) => _events.Clear( );

    public void Upload( PendingUpload upload ) => _events.Add( $"upload {upload.BlobId}" );

    public void AnalyzeLater( long blobId ) => _events.Add( $"analyze {blobId}" );

    public void PurgeLater( long blobId ) => _events.Add( $"purge {blobId}" );

    public void PushMessageLater( long roomId, long messageId ) => _events.Add( $"push {messageId}" );

    public void DeliverLater( long botId, long messageId ) => _events.Add( $"webhook {botId}" );

    public void RemoveLater( long userId ) => _events.Add( $"remove {userId}" );

    public void Disconnect( long userId, bool reconnect ) => _events.Add( $"disconnect {userId}" );
}

/// <summary>
/// The body text hook over the real rich text pipeline (AngleSharp's tokenizer and Campfire's tree builder), with no
/// attachables: the smoke's bodies mention nobody.
/// </summary>
internal sealed class PipelineBodyText : IMessageBodyText
{
    private static readonly RichTextContext Context = new( new NoAttachables( ), RequestHost: null );

    public string ToPlainText( string storedBody, Func<long, string?> userName ) => RichTextPipeline.ToPlainText( storedBody, Context );

    public IReadOnlyList<long> MentionedUserIds( string storedBody ) => [.. RichTextPipeline.MentionedUsers( storedBody, Context ).Select( user => user.Id )];

    private sealed class NoAttachables : IAttachableResolver
    {
        public SignedAttachableLookup LocateSigned( string signedGlobalId ) => SignedAttachableLookup.Invalid;

    }
}
