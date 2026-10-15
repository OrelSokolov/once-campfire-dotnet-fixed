using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;

using Dapper;

namespace Campfire.Application.Users;

/// <summary>Writes to <c>users</c> (<c>app/models/user.rb</c>).</summary>
public static class UserOperations
{
    /// <summary>The polymorphic <c>record_type</c> of a user's avatar.</summary>
    public const string RecordType = ModelNames.User;

    /// <summary>
    /// <c>User</c>'s commit callbacks in definition order: <c>include Avatar, …, Transferable</c> sets up
    /// <c>Avatar</c>'s upload (<see cref="UserAvatars.UploadOrder"/>) before the class body's
    /// <c>after_create_commit :grant_membership_to_open_rooms</c>.
    /// </summary>
    private const int GrantMembershipToOpenRoomsOrder = 2;

    /// <summary>Deactivation closes the user's sockets once more after every other commit callback of the user.</summary>
    private const int CloseRemoteConnectionsAgainOrder = int.MaxValue;

    private const string InsertSql =
        "INSERT INTO users (bio, bot_token, created_at, email_address, name, password_digest, role, status, updated_at) " +
        "VALUES (@Bio, @BotToken, @CreatedAt, @EmailAddress, @Name, @PasswordDigest, @Role, @Status, @UpdatedAt) RETURNING id";

    /// <summary>
    /// <c>User.create!</c>: the user row, with avatar blob and attachment rows when an avatar is given. After commit,
    /// in order: 1. the avatar upload; 2. <c>grant_membership_to_open_rooms</c>; 3. the avatar's analysis job.
    /// </summary>
    /// <exception cref="RecordNotUniqueException">The email address is taken.</exception>
    public static User Create( WriteScope scope, NewUser attributes )
    {
        var user = Insert( scope, attributes );
        RegisterCreated( scope, user, attributes );
        return user;
    }

    /// <summary>
    /// <c>user.update(attributes)</c>: only changed columns, plus <c>updated_at</c>, and nothing when nothing changed.
    /// A new avatar replaces the old one, touches the user, and uploads after commit.
    /// </summary>
    /// <exception cref="RecordNotUniqueException">The email address is taken.</exception>
    public static User Update( WriteScope scope, long userId, UserChanges changes )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( changes );

        var user = UserQueries.Find( scope.Db, userId );
        var original = user.Copy( );
        var key = RecordKeys.User( user.Id );
        scope.Enlist( key );

        Apply( user, changes );
        SaveIfChanged( scope, original, user );

        UserAvatars.Assign( scope, user.Id, changes.Avatar, changes.AvatarBlob );
        return user;
    }

    /// <summary>
    /// <c>deactivate</c>: deletes non-direct memberships, push subscriptions, searches and sessions; marks the user
    /// deactivated and rewrites the email address. Sockets close only after commit, so rollback has no external side
    /// effect.
    /// </summary>
    public static User Deactivate( WriteScope scope, long userId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var user = UserQueries.Find( scope.Db, userId );
        var original = user.Copy( );
        var key = RecordKeys.User( user.Id );
        scope.Enlist( key );

        // memberships.without_direct_rooms.delete_all, then push_subscriptions, searches and sessions.delete_all.
        var db = scope.Db;
        db.Connection.Execute(
            "DELETE FROM memberships WHERE memberships.user_id = @userId AND EXISTS " +
            "(SELECT 1 FROM rooms WHERE rooms.id = memberships.room_id AND rooms.type <> @direct)",
            new
            {
                userId,
                direct = RoomType.Direct
            },
            db.Transaction );
        db.Connection.Execute( "DELETE FROM push_subscriptions WHERE user_id = @userId", new
        {
            userId
        }, db.Transaction );
        db.Connection.Execute( "DELETE FROM searches WHERE user_id = @userId", new
        {
            userId
        }, db.Transaction );
        db.Connection.Execute( "DELETE FROM sessions WHERE user_id = @userId", new
        {
            userId
        }, db.Transaction );

        user.Status = UserStatus.Deactivated;
        user.EmailAddress = DeactivatedEmailAddress( user.EmailAddress );
        SaveIfChanged( scope, original, user );

        scope.AfterCommit( key, CloseRemoteConnectionsAgainOrder, "close remote connections again",
            context => context.Hooks.RemoteConnections.Disconnect( userId, reconnect: false ) );
        return user;
    }

    /// <summary><c>email_address&amp;.gsub(/@/, "-deactivated-#{SecureRandom.uuid}@")</c>.</summary>
    internal static string? DeactivatedEmailAddress( string? emailAddress ) =>
        emailAddress?.Replace( "@", $"-deactivated-{SecureTokens.Uuid( )}@", StringComparison.Ordinal );

    /// <summary>Inserts the user row, and its avatar rows, without enlisting (see <see cref="Accounts.FirstRun"/>).</summary>
    internal static User Insert( WriteScope scope, NewUser attributes )
    {
        var now = scope.Now;
        var user = new User
        {
            Name = attributes.Name,
            EmailAddress = attributes.EmailAddress,
            PasswordDigest = attributes.PasswordDigest,
            Role = attributes.Role,
            Status = UserStatus.Active,
            Bio = attributes.Bio,
            BotToken = attributes.BotToken,
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.Id = DataWrites.Save( ( ) => scope.Db.Connection.ExecuteScalar<long>( InsertSql, user, scope.Db.Transaction ) );
        return user;
    }

    /// <summary>Enlists a new user and queues its commit callbacks, attaching the avatar first when there is one.</summary>
    internal static void RegisterCreated( WriteScope scope, User user, NewUser attributes )
    {
        var key = RecordKeys.User( user.Id );
        scope.Enlist( key );

        UserAvatars.Assign( scope, user.Id, attributes.Avatar, attributes.AvatarBlob );

        var userId = user.Id;
        scope.AfterCommit( key, GrantMembershipToOpenRoomsOrder, "grant_membership_to_open_rooms", context =>
        {
            var openRoomIds = context.Db.Connection.Query<long>(
                    "SELECT rooms.id FROM rooms WHERE rooms.type = @open ORDER BY rooms.id", new
                    {
                        open = RoomType.Open
                    }, context.Db.Transaction )
                .AsList( );
            MembershipGrants.GrantRoomsTo( context.Db, userId, openRoomIds );
        } );
    }

    /// <summary>
    /// A user's <c>touch</c>: the avatar attachment's <c>belongs_to :record, touch: true</c> (deferred to before commit
    /// by <see cref="TouchLater"/>) and <c>Blob#touch_attachments</c>.
    /// </summary>
    internal static void Touch( CampfireDb db, long userId, DateTime touchedAt ) =>
        db.Connection.Execute( "UPDATE users SET updated_at = @at WHERE id = @userId", new
        {
            at = touchedAt,
            userId
        }, db.Transaction );

    /// <summary><c>touch_later</c>: the user's <see cref="Touch"/>, just before commit, with the time of this request.</summary>
    internal static void TouchLater( WriteScope scope, long userId )
    {
        var touchedAt = scope.Now;
        scope.BeforeCommit( RecordKeys.User( userId ), CommitOrder.Touch, commitScope => Touch( commitScope.Db, userId, touchedAt ) );
    }

    private static void Apply( User user, UserChanges changes )
    {
        if ( changes.Name.IsSet )
        {
            user.Name = changes.Name.Value;
        }

        if ( changes.EmailAddress.IsSet )
        {
            user.EmailAddress = changes.EmailAddress.Value;
        }

        if ( changes.PasswordDigest.IsSet )
        {
            user.PasswordDigest = changes.PasswordDigest.Value;
        }

        if ( changes.Role.IsSet )
        {
            user.Role = changes.Role.Value;
        }

        if ( changes.Bio.IsSet )
        {
            user.Bio = changes.Bio.Value;
        }
    }

    /// <summary><c>save</c>: the columns changed since <paramref name="original"/> was read, and <c>updated_at</c>, or nothing.</summary>
    /// <exception cref="RecordNotUniqueException">The email address or bot token is taken.</exception>
    internal static void SaveIfChanged( WriteScope scope, User original, User user )
    {
        var changes = new ChangedColumns( TableNames.Users );
        changes.Compare( "bio", nameof( User.Bio ), original.Bio, user.Bio );
        changes.Compare( "bot_token", nameof( User.BotToken ), original.BotToken, user.BotToken );
        changes.Compare( "email_address", nameof( User.EmailAddress ), original.EmailAddress, user.EmailAddress );
        changes.Compare( "name", nameof( User.Name ), original.Name, user.Name );
        changes.Compare( "password_digest", nameof( User.PasswordDigest ), original.PasswordDigest, user.PasswordDigest );
        changes.Compare( "role", nameof( User.Role ), original.Role, user.Role );
        changes.Compare( "status", nameof( User.Status ), original.Status, user.Status );
        if ( !changes.Any )
        {
            return;
        }

        user.UpdatedAt = scope.Now;
        var rows = DataWrites.Save( ( ) => scope.Db.Connection.Execute( changes.UpdateSql( ), user, scope.Db.Transaction ) );
        DataWrites.ExpectOneRow( rows, RecordKeys.User( user.Id ) );
    }
}
