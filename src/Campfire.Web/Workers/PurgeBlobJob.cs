namespace Campfire.Web.Workers;

/// <summary><c>ActiveStorage::PurgeJob.perform_later(blob)</c>.</summary>
public sealed record PurgeBlobJob( long BlobId );
