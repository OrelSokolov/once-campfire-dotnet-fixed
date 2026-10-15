namespace Campfire.IntegrationTests.Support;

/// <summary>A file field for <see cref="FormRequests.UploadFormAsync"/>.</summary>
/// <param name="Field">The field's name, e.g. <c>user[avatar]</c>.</param>
/// <param name="FileName">The file's name.</param>
/// <param name="ContentType">The part's content type.</param>
/// <param name="Bytes">The file's bytes.</param>
public sealed record FileField( string Field, string FileName, string ContentType, byte[] Bytes );
