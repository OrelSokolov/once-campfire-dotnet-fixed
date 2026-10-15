namespace Campfire.Application.Data.Hooks;

/// <summary>
/// The side effects that leave the database, injected where the operations call them. Operations call each hook
/// at the point Rails performs the effect: most after commit, in callback order; remote disconnects for
/// deactivation and bans also inside the transaction, as Rails does. Every member is required; a host that does not
/// yet support an effect must say so by throwing, not by dropping it.
/// </summary>
/// <remarks>
/// Hooks run synchronously on the thread that holds the writer lease, so implementations hand work off (a bounded
/// channel write, a socket close request) instead of doing it inline. Each job kind defines its full-queue policy.
/// </remarks>
public sealed record CampfireHooks(
    IMessageBodyText BodyText,
    IAttachmentUploads Uploads,
    IStorageJobs StorageJobs,
    IPushJobs PushJobs,
    IWebhookJobs WebhookJobs,
    IBannedContentJobs BannedContentJobs,
    IRemoteConnections RemoteConnections );

/// <summary>What the data layer needs from Action Text (the rich text component implements it).</summary>
public interface IMessageBodyText
{
    /// <summary>
    /// <c>ActionText::Content#to_plain_text</c> of a stored body. Mention attachments render as
    /// <c>attachable_plain_text_representation</c>, <c>"@#{name}"</c>; <paramref name="userName"/> looks a user's
    /// name up on the caller's connection (never another one: that could deadlock the lease pool).
    /// </summary>
    string ToPlainText( string storedBody, Func<long, string?> userName );

    /// <summary>
    /// <c>body.attachables.grep(User).uniq</c>: the ids of mentioned users, in document order, without duplicates
    /// (<c>app/models/message/mentionee.rb</c>).
    /// </summary>
    IReadOnlyList<long> MentionedUserIds( string storedBody );
}

/// <summary>
/// <c>has_one_attached</c>'s <c>after_commit</c> upload: the new blob's file is written only once the rows commit.
/// </summary>
public interface IAttachmentUploads
{
    void Upload( PendingUpload upload );
}

/// <summary>
/// A blob to upload after commit. <see cref="Source"/> is whatever the caller handed the operation for the bytes
/// (the storage component defines its type, e.g. a spooled request body).
/// </summary>
public sealed record PendingUpload( long BlobId, string Key, string? Checksum, object Source );

/// <summary>Active Storage's own jobs.</summary>
public interface IStorageJobs
{
    /// <summary><c>ActiveStorage::AnalyzeJob</c>, from <c>Attachment#analyze_blob_later</c> for an unanalyzed blob.</summary>
    void AnalyzeLater( long blobId );

    /// <summary><c>ActiveStorage::PurgeJob</c>, from <c>Attachment#purge_dependent_blob_later</c>.</summary>
    void PurgeLater( long blobId );
}

/// <summary><c>Room::PushMessageJob.perform_later(room, message)</c>, from <c>Room#receive</c>.</summary>
public interface IPushJobs
{
    void PushMessageLater( long roomId, long messageId );
}

/// <summary><c>Bot::WebhookJob.perform_later(bot, message)</c>, from <c>User::Bot#deliver_webhook_later</c>.</summary>
public interface IWebhookJobs
{
    void DeliverLater( long botId, long messageId );
}

/// <summary><c>RemoveBannedContentJob.perform_later(user)</c>, from <c>User::Bannable#apply_ban</c>.</summary>
public interface IBannedContentJobs
{
    void RemoveLater( long userId );
}

/// <summary>
/// <c>ActionCable.server.remote_connections.where(current_user: user).disconnect(reconnect:)</c>.
/// <c>reconnect: true</c> comes from <c>reset_remote_connections</c> (membership removed, sign-out);
/// <c>false</c> from deactivation and bans.
/// </summary>
public interface IRemoteConnections
{
    void Disconnect( long userId, bool reconnect );
}
