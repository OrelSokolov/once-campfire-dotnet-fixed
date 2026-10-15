using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Storage;

namespace Campfire.Application.Users;

/// <summary>
/// <c>User::Avatar</c>'s <c>has_one_attached :avatar</c>: assigning and removing a user's avatar, each of which touches
/// the user (the attachment's <c>belongs_to :record, touch: true</c>).
/// </summary>
public static class UserAvatars
{
    /// <summary><c>has_one_attached :avatar</c>.</summary>
    public const string AttachmentName = "avatar";

    /// <summary>
    /// The avatar upload's place among <c>User</c>'s commit callbacks: <c>include Avatar</c> comes first, so its upload
    /// runs before the class body's callbacks.
    /// </summary>
    internal const int UploadOrder = 1;

    /// <summary>The holder of <paramref name="userId"/>'s avatar.</summary>
    public static AttachmentOwner Owner( long userId ) => new( UserOperations.RecordType, userId );

    /// <summary>
    /// <c>Current.user.avatar.destroy</c> (<c>Users::AvatarsController#destroy</c>): the attachment goes, its blob is
    /// purged after commit, and the user is touched.
    /// </summary>
    public static void Remove( WriteScope scope, long userId )
    {
        if ( AttachedOne.Remove( scope, Owner( userId ), AttachmentName ) )
        {
            UserOperations.TouchLater( scope, userId );
        }
    }

    /// <summary>
    /// <c>user.avatar = attachable</c> saved: an upload's rows, touching the user, with the upload after commit; or a
    /// directly uploaded blob's attachment, touching the user when it is new, with nothing to upload.
    /// </summary>
    internal static void Assign( WriteScope scope, long userId, NewAttachment? upload, SignedBlobAttachment? blob ) =>
        AttachedOne.Assign( scope, Owner( userId ), AttachmentName, upload, blob, RecordKeys.User( userId ), UploadOrder, UserOperations.TouchLater );
}
