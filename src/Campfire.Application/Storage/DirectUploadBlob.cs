namespace Campfire.Application.Storage;

/// <summary>What <c>ActiveStorage::DirectUploadsController#blob_args</c> permitted, as the columns cast it.</summary>
/// <param name="Filename">The filename; <c>null</c> when not given.</param>
/// <param name="ByteSize">The declared size; <c>null</c> when not given or not an integer.</param>
/// <param name="Checksum">The Base64 MD5 the upload must match.</param>
/// <param name="ContentType">The declared content type.</param>
/// <param name="Metadata">The metadata column's JSON (<c>{}</c> when none was given).</param>
public sealed record DirectUploadBlob( string? Filename, long? ByteSize, string? Checksum, string? ContentType, string Metadata );
