using Campfire.Application.Accounts;
using Campfire.Application.Data;

namespace Campfire.Web.Endpoints;

/// <summary><c>Current.account</c>, which is <c>Account.first</c> whenever it's read.</summary>
public static class CurrentAccount
{
    /// <summary>The account, where the action calls <paramref name="method"/> on it (nil raises <c>NoMethodError</c>).</summary>
    public static Account Require( CampfireDb db, string method )
    {
        var account = AccountQueries.First( db );
        if ( account is null )
        {
            throw new InvalidOperationException( $"undefined method '{method}' for nil" );
        }

        return account;
    }
}
