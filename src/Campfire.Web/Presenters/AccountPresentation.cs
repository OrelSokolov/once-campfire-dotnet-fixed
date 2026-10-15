using Campfire.Application.Data;
using Campfire.Application.Users;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Presenters;

/// <summary>The account-wide facts the pages show.</summary>
public static class AccountPresentation
{
    /// <summary><c>User.administrator.first</c> for <c>accounts/_help_contact</c>: the first administrator by id, if any.</summary>
    public static HelpContact? HelpContact( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return UserQueries.FirstAdministrator( db ) is { } owner ? new HelpContact( owner.Name, owner.EmailAddress ?? "" ) : null;
    }
}
