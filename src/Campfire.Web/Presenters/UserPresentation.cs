using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.RichText;
using Campfire.Application.Security;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Paths;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Presenters;

/// <summary>A <c>users</c> row as the views show it: avatars, titles, summaries and mentions.</summary>
public static class UserPresentation
{
    /// <summary>A database <c>datetime</c> (UTC) as an instant.</summary>
    public static DateTimeOffset AsUtc( DateTime time ) => new( DateTime.SpecifyKind( time, DateTimeKind.Utc ) );

    /// <summary><c>user.avatar_token</c>: <c>signed_id(purpose: :avatar)</c>, which never expires.</summary>
    public static string AvatarToken( CampfireApp app, long userId )
    {
        ArgumentNullException.ThrowIfNull( app );

        return app.SignedIds.Generate( ModelNames.User, userId, SignedIds.AvatarPurpose );
    }

    /// <summary><c>fresh_user_avatar_path(user)</c>.</summary>
    public static string AvatarPath( CampfireApp app, User user )
    {
        ArgumentNullException.ThrowIfNull( user );

        return Routes.FreshUserAvatar( AvatarToken( app, user.Id ), RubyText.NumberTimestamp( AsUtc( user.UpdatedAt ) ) );
    }

    /// <summary>What avatars, author headings and boosts show.</summary>
    public static UserView View( CampfireApp app, User user )
    {
        ArgumentNullException.ThrowIfNull( user );

        return new UserView( user.Id, user.Name, user.Title, AvatarPath( app, user ) ) { Version = AsUtc( user.UpdatedAt ) };
    }

    /// <summary><c>Current.user</c> as the layout sees it.</summary>
    public static CurrentUserView Current( CampfireApp app, User user )
    {
        ArgumentNullException.ThrowIfNull( user );

        return new CurrentUserView( user.Id, user.Name, user.CanAdminister( ), user.IsBot, AvatarPath( app, user ) );
    }

    /// <summary>A user row as the users, accounts and sidebar views see it.</summary>
    public static UserSummary Summary( CampfireApp app, User user )
    {
        ArgumentNullException.ThrowIfNull( user );

        return new UserSummary
        {
            Id = user.Id,
            Version = AsUtc( user.UpdatedAt ),
            Name = user.Name,
            Bio = user.Bio,
            EmailAddress = user.EmailAddress,
            Role = user.Role,
            Status = user.Status,
            AvatarPath = AvatarPath( app, user ),
        };
    }

    /// <summary>
    /// Display fields consumed only by <c>users/sidebars/rooms/_direct</c>; profile and permission views use Summary.
    /// </summary>
    public static UserSummary SidebarMember( CampfireApp app, DirectRoomMember member ) => new( )
    {
        Id = member.Id,
        Version = AsUtc( member.UpdatedAt ),
        Name = member.Name,
        AvatarPath = Routes.FreshUserAvatar( AvatarToken( app, member.Id ), RubyText.NumberTimestamp( AsUtc( member.UpdatedAt ) ) ),
    };

    /// <summary>A user as rich text renders a mention (<c>users/_mention</c>).</summary>
    public static MentionUser Mention( CampfireApp app, User user )
    {
        ArgumentNullException.ThrowIfNull( app );
        ArgumentNullException.ThrowIfNull( user );

        return new MentionUser(
            user.Id,
            user.Name,
            user.Title,
            app.SignedGlobalIds.AttachableSignedGlobalId( GlobalId.For( ModelNames.User, user.Id ) ),
            Routes.User( user.Id ),
            AvatarPath( app, user ) );
    }
}
