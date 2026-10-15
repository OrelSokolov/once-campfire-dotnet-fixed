namespace Campfire.Application.Data.Records;

/// <summary>
/// What <c>ActiveStorage::Blob#identify_without_saving</c> assigns to a blob that wasn't identified: the content type
/// Marcel gives its first 4 KiB, and the metadata with <c>identified: true</c>, as the column stores it.
/// </summary>
/// <param name="ContentType">The identified content type.</param>
/// <param name="Metadata">The metadata JSON to store.</param>
public sealed record BlobIdentity( string? ContentType, string Metadata );
