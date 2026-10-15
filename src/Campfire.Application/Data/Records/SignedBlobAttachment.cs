namespace Campfire.Application.Data.Records;

/// <summary>A blob found by its verified signed id, and what identifying it assigned (null when it was identified).</summary>
/// <param name="BlobId">The blob.</param>
/// <param name="Identity">The content type and metadata <c>identify_without_saving</c> assigned.</param>
public sealed record SignedBlobAttachment( long BlobId, BlobIdentity? Identity );
