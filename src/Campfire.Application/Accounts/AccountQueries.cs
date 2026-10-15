using Campfire.Application.Data;
using Campfire.Application.Data.Records;

using Dapper;

namespace Campfire.Application.Accounts;

/// <summary>Reads of <c>accounts</c>.</summary>
public static class AccountQueries
{
    /// <summary><c>Account.find(id)</c>.</summary>
    /// <exception cref="RecordNotFoundException">No such account.</exception>
    internal static Account Find( CampfireDb db, long accountId ) =>
        db.Connection.QuerySingleOrDefault<Account>(
            $"SELECT {Columns.Accounts} FROM accounts WHERE accounts.id = @accountId", new
            {
                accountId
            }, db.Transaction ).OrNotFound( ModelNames.Account );

    /// <summary><c>Account.first</c> (<c>Current.account</c>).</summary>
    public static Account? First( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Account>(
            $"SELECT {Columns.Accounts} FROM accounts ORDER BY accounts.id LIMIT 1", transaction: db.Transaction );
    }

    /// <summary><c>account.logo.attached?</c>.</summary>
    public static bool HasLogo( CampfireDb db, long accountId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.ExecuteScalar<bool>(
            $"SELECT EXISTS (SELECT 1 FROM active_storage_attachments WHERE {AttachedOne.WhereOwnerAndName})",
            new
            {
                recordType = AccountOperations.RecordType,
                recordId = accountId,
                name = AccountOperations.LogoName
            },
            db.Transaction );
    }
}
