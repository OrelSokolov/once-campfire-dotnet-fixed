namespace Campfire.Application.Storage.Disk;

/// <summary>The payload of a direct upload's <c>encoded_token</c> (purpose <c>blob_token</c>), as <c>DiskController#update</c> reads it.</summary>
public sealed record DiskToken( string Key, string? ContentType, long ContentLength, string Checksum, string ServiceName );
