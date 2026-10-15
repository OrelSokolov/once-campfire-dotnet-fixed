using Campfire.Application.Sessions;
using Campfire.Application.Users;
using Campfire.Web.Policy;
using Campfire.Web.Security;

namespace Campfire.Web.Views.Caching;

/// <summary>Only immutable fields needed by page admission and the viewer key survive the read.</summary>
internal sealed record PageSessionAuthority( CurrentSession KeySession, UserRole Role, UserStatus Status, DateTime LastActiveAt )
{
    internal static PageSessionAuthority From( CurrentSession session )
    {
        var user = ( User ) session.User.Record!;
        var record = ( Session ) session.Record!;
        var keySession = new CurrentSession( session.Id, session.Token, new CurrentUser( session.User.Id ) );
        return new( keySession, user.Role, user.Status, record.LastActiveAt );
    }

    internal bool Accepts( string token, DateTime now ) =>
        Status == UserStatus.Active
        && Role != UserRole.Bot
        && BrowserAuthentication.MatchesToken( token, KeySession.Token )
        && LastActiveAt >= now - Session.ActivityRefreshRate;
}
