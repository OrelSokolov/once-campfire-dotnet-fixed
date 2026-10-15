using Campfire.Application.Data.Hooks;
using Campfire.Application.Storage;

namespace Campfire.Web.App;

/// <summary>
/// <c>has_one_attached</c>'s after-commit upload: the bytes were staged before the transaction
/// (<see cref="BlobStorage.StageUploadAsync"/>); once the rows commit, the staged file is renamed to its key's path
/// and handed to the blob. A rename is quick, so it runs inline on the committing thread, in the commit order of
/// after-commit ordering (after the search index insert, before <c>room.receive</c>).
/// </summary>
public sealed class StagedUploads : IAttachmentUploads
{
    public void Upload( PendingUpload upload )
    {
        ArgumentNullException.ThrowIfNull( upload );

        if ( upload.Source is not StagedBlob staged )
        {
            throw new InvalidOperationException( $"Blob {upload.BlobId} has no staged file to upload (got {upload.Source.GetType( ).Name})." );
        }

        if ( staged.Blob.Key != upload.Key )
        {
            throw new InvalidOperationException( $"Blob {upload.BlobId}'s staged file belongs to another key." );
        }

        staged.Publish( );
        staged.Keep( );
    }
}
