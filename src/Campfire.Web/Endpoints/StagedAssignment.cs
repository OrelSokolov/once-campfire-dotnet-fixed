using Campfire.Application.Data.Records;
using Campfire.Application.Storage;
using Campfire.Web.App;

namespace Campfire.Web.Endpoints;

/// <summary>
/// An <see cref="AttachmentAssignment"/> made ready before the write: its upload staged, or its signed blob resolved.
/// Dispose it after the write, whatever happened.
/// </summary>
public sealed class StagedAssignment : IDisposable
{
    private static readonly StagedAssignment NotGiven = new( AttachmentAssignment.NotGiven, null, null );

    private static readonly StagedAssignment Removal = new( AttachmentAssignment.Removal, null, null );

    private StagedAssignment( AttachmentAssignment assignment, StagedBlob? upload, SignedBlobAttachment? signedBlob )
    {
        Assignment = assignment;
        Upload = upload;
        SignedBlob = signedBlob;
    }

    public AttachmentAssignment Assignment { get; }

    /// <summary>The staged upload of a <see cref="AttachmentAssignment.Create"/>, owned by this; otherwise null.</summary>
    public StagedBlob? Upload { get; }

    /// <summary>The new attachment's row, for a <see cref="AttachmentAssignment.Create"/>; otherwise null.</summary>
    public NewAttachment? Attachment => Upload is null ? null : AttachmentAssignment.ToNewAttachment( Upload );

    /// <summary>The blob to attach, for an <see cref="AttachmentAssignment.Attach"/>; otherwise null.</summary>
    public SignedBlobAttachment? SignedBlob { get; }

    /// <summary>Whether the save destroys the current attachment.</summary>
    public bool Deletes => Assignment is AttachmentAssignment.Delete;

    /// <summary>
    /// Stages the upload of a <see cref="AttachmentAssignment.Create"/> and resolves the blob of an
    /// <see cref="AttachmentAssignment.Attach"/>; the other assignments carry neither, and take no allocation.
    /// </summary>
    public static ValueTask<StagedAssignment> StageAsync( CampfireApp app, AttachmentAssignment assignment, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( assignment );

        return assignment switch
        {
            AttachmentAssignment.Create create => StageUploadAsync( app, create, cancellationToken ),
            AttachmentAssignment.Attach attach => ResolveSignedBlobAsync( app, attach, cancellationToken ),
            AttachmentAssignment.Delete => ValueTask.FromResult( Removal ),
            _ => ValueTask.FromResult( NotGiven ),
        };
    }

    public void Dispose( ) => Upload?.Dispose( );

    private static async ValueTask<StagedAssignment> StageUploadAsync(
        CampfireApp app,
        AttachmentAssignment.Create create,
        CancellationToken cancellationToken )
    {
        var upload = await AttachmentAssignment.StageAsync( app, create.File, cancellationToken );
        return new StagedAssignment( create, upload, signedBlob: null );
    }

    private static async ValueTask<StagedAssignment> ResolveSignedBlobAsync(
        CampfireApp app,
        AttachmentAssignment.Attach attach,
        CancellationToken cancellationToken )
    {
        var signedBlob = await AttachmentAssignment.ResolveSignedBlobAsync( app, attach.SignedId, cancellationToken );
        return new StagedAssignment( attach, upload: null, signedBlob );
    }
}
