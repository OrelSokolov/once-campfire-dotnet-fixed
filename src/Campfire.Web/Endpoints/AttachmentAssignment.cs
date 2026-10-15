using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Storage;
using Campfire.Web.App;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints;

/// <summary>
/// What assigning a permitted param to a <c>has_one_attached</c> attribute does (<c>user.avatar = value</c>,
/// <c>account.logo = value</c>, <c>message.attachment = value</c>; <c>ActiveStorage::Attached::Model#&lt;name&gt;=</c>):
/// a missing key changes nothing, nil or <c>""</c> destroys the attachment on save (<c>Changes::DeleteOne</c>), and an
/// uploaded file or a directly uploaded blob's signed id replaces it (<c>Changes::CreateOne</c>). Every attachment
/// param goes through here, and every signed id through <see cref="ResolveSignedBlobAsync"/>.
/// </summary>
public abstract record AttachmentAssignment
{
    private AttachmentAssignment( )
    {
    }

    /// <summary>The assignment of a param that wasn't given; it carries nothing, so one instance serves every request.</summary>
    internal static AttachmentAssignment NotGiven { get; } = new Unchanged( );

    /// <summary>The assignment of nil or <c>""</c>; it carries nothing, so one instance serves every request.</summary>
    internal static AttachmentAssignment Removal { get; } = new Delete( );

    /// <summary>The key wasn't given.</summary>
    public sealed record Unchanged : AttachmentAssignment;

    /// <summary>nil or <c>""</c>: the attachment goes.</summary>
    public sealed record Delete : AttachmentAssignment;

    /// <summary>An uploaded file becomes the attachment.</summary>
    public sealed record Create( UploadedFile File ) : AttachmentAssignment;

    /// <summary>Any other string: the signed id of a blob already in storage (a direct upload's) becomes the attachment.</summary>
    public sealed record Attach( string SignedId ) : AttachmentAssignment;

    /// <summary>
    /// The assignment <paramref name="permitted"/>'s <paramref name="key"/> makes. A string that isn't blank is a signed
    /// blob id (<c>ActiveStorage::Blob.find_signed!</c>, resolved by <see cref="ResolveSignedBlobAsync"/>). Any other
    /// permitted scalar (a number, a boolean) isn't an attachable: <c>ArgumentError</c>, a 500.
    /// </summary>
    public static AttachmentAssignment From( InputObject permitted, string key )
    {
        ArgumentNullException.ThrowIfNull( permitted );

        if ( !permitted.ContainsKey( key ) )
        {
            return NotGiven;
        }

        return permitted[key] switch
        {
            null or InputNull => Removal,
            InputText { Value.Length: 0 } when key is ParamNames.Users.Avatar or ParamNames.Accounts.Logo => NotGiven,
            InputText { Value.Length: 0 } => Removal,
            InputFile file => new Create( file.File ),
            InputText signedId => new Attach( signedId.Value ),
            var other => throw new ArgumentException( $"Could not find or build blob: expected attachable, got {other}" ),
        };
    }

    /// <summary>
    /// <c>CreateOne</c> with a <c>String</c>: <c>ActiveStorage::Blob.find_signed!(signed_id)</c>, then
    /// <c>identify_without_saving</c>, both before any lease. A bad signature raises
    /// <c>ActiveSupport::MessageVerifier::InvalidSignature</c> (500); a blob that is gone raises <c>RecordNotFound</c>
    /// (404, unless the controller rescues it).
    /// </summary>
    public static async Task<SignedBlobAttachment> ResolveSignedBlobAsync( CampfireApp app, string signedId, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( app );
        ArgumentNullException.ThrowIfNull( signedId );

        var blobId = app.StorageVerifier.VerifySignedBlobId( signedId, app.Now );
        if ( blobId is null )
        {
            throw new StorageException( StorageFailure.InvalidSignature, "ActiveSupport::MessageVerifier::InvalidSignature" );
        }

        var blob = await app.Database.ReadAsync( db => BlobRecords.FindById( db, blobId.Value ), cancellationToken );
        if ( blob is null )
        {
            throw new RecordNotFoundException( ModelNames.Blob );
        }

        var identity = await app.Storage.IdentifyWithoutSavingAsync( blob, cancellationToken );
        return new SignedBlobAttachment( blob.Id, identity );
    }

    /// <summary>
    /// The file half of the assignment, before any lease: the upload staged (copied, checksummed, identified) as a new
    /// blob. The caller disposes it; the after-commit upload keeps the file.
    /// </summary>
    public static async Task<StagedBlob> StageAsync( CampfireApp app, UploadedFile upload, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( app );
        ArgumentNullException.ThrowIfNull( upload );

        await using var content = upload.OpenRead( );
        return await app.Storage.StageUploadAsync( content, new BlobFilename( upload.OriginalFilename ), upload.ContentType, cancellationToken );
    }

    /// <summary>The blob row for a staged upload, whose bytes the after-commit upload publishes.</summary>
    public static NewAttachment ToNewAttachment( StagedBlob staged )
    {
        ArgumentNullException.ThrowIfNull( staged );

        return new NewAttachment
        {
            Key = staged.Blob.Key,
            Filename = staged.Blob.Filename.Raw,
            ContentType = staged.Blob.ContentType,
            Metadata = staged.Blob.Metadata.ToJson( ),
            ServiceName = staged.Blob.ServiceName,
            ByteSize = staged.Blob.ByteSize,
            Checksum = staged.Blob.Checksum,
            UploadSource = staged,
        };
    }
}
