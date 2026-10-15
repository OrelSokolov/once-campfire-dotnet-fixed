namespace Campfire.Application.Storage.Disk;

/// <summary>The payload of a disk URL's <c>encoded_key</c> (purpose <c>blob_key</c>), as <c>DiskController#show</c> reads it.</summary>
public sealed record DiskKey( string Key, string Disposition, string? ContentType, string ServiceName );
