using Campfire.Application.Data;
using Campfire.Application.Users;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Users;

/// <summary>
/// The user forms' strong parameters (<c>params.require(:user).permit(...)</c>) and how their values assign to
/// <c>User</c>'s attributes: strings as Active Model's string type casts them, and the password through
/// native Identity password hashes, computed before any lease is taken.
/// </summary>
public static class UserParams
{
    /// <summary>The form's param key (<c>form_with model: @user</c>).</summary>
    public const string Key = ParamNames.Users.Key;

    /// <summary>The column a user's name is stored in, which is <c>NOT NULL</c>.</summary>
    public const string NameColumn = "users.name";

    /// <summary><c>params.require(:user).permit(*keys)</c> (<see cref="StrongParams.Permit"/>).</summary>
    public static InputObject Permit( RequestContext requestContext, params IReadOnlyList<string> keys )
    {
        ArgumentNullException.ThrowIfNull( keys );

        return StrongParams.Permit( requestContext, Key, keys.Select( InputField.Key ) );
    }

    /// <summary>
    /// A string attribute's assignment: unset when the key is missing, null for nil, and the value as
    /// <c>ActiveModel::Type::String</c> casts it (<c>true</c> is <c>"t"</c>, a number its <c>to_s</c>).
    /// </summary>
    public static Assigned<string?> Text( InputObject permitted, string key )
    {
        ArgumentNullException.ThrowIfNull( permitted );

        if ( !permitted.ContainsKey( key ) )
        {
            return default;
        }

        return permitted[key] switch
        {
            null or InputNull => new Assigned<string?>( null ),
            InputText text => text.Value,
            InputBoolean flag => flag.Value ? "t" : "f",
            InputNumber number => number.ToText( ),
            var other => throw new InvalidOperationException( $"{key} can't be assigned a {other.GetType( ).Name}" ),
        };
    }

    /// <summary>
    /// <c>password=</c> (<c>has_secure_password validations: false</c>): nil clears the digest, a blank string leaves
    /// it alone (unset here), and anything else uses the native Identity password hasher.
    /// </summary>
    public static Assigned<string?> PasswordDigest( InputObject permitted )
    {
        ArgumentNullException.ThrowIfNull( permitted );

        if ( !permitted.ContainsKey( ParamNames.Users.Password ) )
        {
            return default;
        }

        return permitted[ParamNames.Users.Password] switch
        {
            null or InputNull => new Assigned<string?>( null ),
            InputText { Value.Length: 0 } => default,
            InputText text => Passwords.Digest( text.Value ),
            var other => throw new InvalidOperationException(
                $"undefined method 'empty?' for an instance of {other.GetType( ).Name}" ),
        };
    }

    /// <summary>
    /// A <c>NOT NULL</c> string column's assignment (<c>users.name</c>, <c>accounts.name</c>): nil fails the save in
    /// Rails (<c>NotNullViolation</c>, a 500, rolling the transaction back); here it fails before anything is written.
    /// </summary>
    public static Assigned<string> NotNullString( InputObject permitted, string key, string column )
    {
        var value = Text( permitted, key );
        if ( !value.IsSet )
        {
            return default;
        }

        if ( value.Value is null )
        {
            throw NotNullViolations.ForColumn( column );
        }

        return value.Value;
    }

    /// <summary>
    /// The sign-up and first-run form, <c>params.require(:user).permit(:name, :avatar, :email_address, :password)</c>:
    /// <c>User.new</c>'s attributes (see <see cref="ToNewUser"/>) and what <c>avatar=</c> does.
    /// </summary>
    public static (NewUser Attributes, AttachmentAssignment Avatar) PermitNewUser( RequestContext requestContext )
    {
        var permitted = Permit( requestContext, ParamNames.Users.Name, ParamNames.Users.Avatar, ParamNames.Users.EmailAddress, ParamNames.Users.Password );
        return (ToNewUser( permitted ), AttachmentAssignment.From( permitted, ParamNames.Users.Avatar ));
    }

    /// <summary>A new user's attributes with its staged avatar (a new file, or a direct upload's blob).</summary>
    public static NewUser WithAvatar( this NewUser attributes, StagedAssignment avatar )
    {
        ArgumentNullException.ThrowIfNull( attributes );
        ArgumentNullException.ThrowIfNull( avatar );

        return attributes with
        {
            Avatar = avatar.Attachment,
            AvatarBlob = avatar.SignedBlob,
        };
    }

    /// <summary>A user's changes with its staged avatar (a new file, or a direct upload's blob).</summary>
    public static UserChanges WithAvatar( this UserChanges changes, StagedAssignment avatar )
    {
        ArgumentNullException.ThrowIfNull( changes );
        ArgumentNullException.ThrowIfNull( avatar );

        return changes with
        {
            Avatar = avatar.Attachment,
            AvatarBlob = avatar.SignedBlob,
        };
    }

    /// <summary>
    /// <c>User.new(name:, email_address:, password:)</c>'s attributes. A missing name is nil, which
    /// <c>users.name NOT NULL</c> refuses (see <see cref="NotNullString"/>).
    /// </summary>
    public static NewUser ToNewUser( InputObject permitted, UserRole role = UserRole.Member )
    {
        var name = NotNullString( permitted, ParamNames.Users.Name, NameColumn );
        if ( !name.IsSet )
        {
            throw NotNullViolations.ForColumn( NameColumn );
        }

        return new NewUser
        {
            Name = name.Value,
            EmailAddress = Text( permitted, ParamNames.Users.EmailAddress ).Value,
            PasswordDigest = PasswordDigest( permitted ).Value,
            Role = role,
        };
    }
}
