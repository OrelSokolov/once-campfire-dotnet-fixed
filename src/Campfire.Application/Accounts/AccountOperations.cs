using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Storage;

using Dapper;

namespace Campfire.Application.Accounts;

/// <summary>Writes to <c>accounts</c> (<c>app/models/account.rb</c>, <c>account/joinable.rb</c>).</summary>
public static class AccountOperations
{
    /// <summary>The polymorphic <c>record_type</c> of the account's logo.</summary>
    public const string RecordType = ModelNames.Account;

    /// <summary><c>has_one_attached :logo</c>.</summary>
    public const string LogoName = "logo";

    /// <summary><c>Account</c>'s only commit callback: <c>has_one_attached :logo</c>'s upload.</summary>
    private const int UploadLogoOrder = 1;

    private const string InsertSql =
        "INSERT INTO accounts (created_at, custom_styles, join_code, name, settings, singleton_guard, updated_at) " +
        "VALUES (@CreatedAt, @CustomStyles, @JoinCode, @Name, @Settings, @SingletonGuard, @UpdatedAt) RETURNING id";

    /// <summary>
    /// <c>Account.create!(name:)</c>: a fresh join code (<c>before_create</c>) and the settings defaults, which
    /// <c>has_json</c>'s <c>before_save</c> writes out.
    /// </summary>
    /// <exception cref="RecordNotUniqueException">An account exists already (the singleton guard).</exception>
    public static Account Create( WriteScope scope, string name )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var now = scope.Now;
        var account = new Account
        {
            Name = name,
            JoinCode = GenerateJoinCode( ),
            Settings = AccountSettings.Materialize( null ),
            SingletonGuard = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        account.Id = DataWrites.Save( ( ) => scope.Db.Connection.ExecuteScalar<long>( InsertSql, account, scope.Db.Transaction ) );
        scope.Enlist( RecordKeys.Account( account.Id ) );
        return account;
    }

    /// <summary><c>reset_join_code</c>: <c>update! join_code: generate_join_code</c>.</summary>
    public static Account ResetJoinCode( WriteScope scope, long accountId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var account = AccountQueries.Find( scope.Db, accountId );
        var original = account.Copy( );
        account.JoinCode = GenerateJoinCode( );
        Save( scope, original, account );
        return account;
    }

    /// <summary>
    /// <c>@account.update!(name:, custom_styles:, settings:, logo:)</c>. Only changed columns are written, plus
    /// <c>updated_at</c>. Every save also writes the settings defaults into a NULL column (<c>has_json</c>'s
    /// <c>before_save</c>).
    /// </summary>
    /// <exception cref="ArgumentException">A settings key is not in the schema.</exception>
    public static Account Update( WriteScope scope, long accountId, AccountChanges changes )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( changes );

        var account = AccountQueries.Find( scope.Db, accountId );
        var original = account.Copy( );
        if ( changes.Name.IsSet )
        {
            account.Name = changes.Name.Value;
        }

        if ( changes.CustomStyles.IsSet )
        {
            account.CustomStyles = changes.CustomStyles.Value;
        }

        if ( changes.Settings is { } values )
        {
            var settings = AccountSettings.Read( account.Settings );
            var storedJson = settings.ToJson( );
            settings.Assign( values );
            var assignedJson = settings.ToJson( );
            if ( account.Settings is null || assignedJson != storedJson )
            {
                account.Settings = assignedJson;
            }
        }

        Save( scope, original, account );
        AttachedOne.Assign(
            scope, Logo( account.Id ), LogoName, changes.Logo, changes.LogoBlob, RecordKeys.Account( account.Id ), UploadLogoOrder, TouchLater );
        return account;
    }

    /// <summary>The logo's removal (<c>Accounts::LogosController#destroy</c>): its blob is purged after commit.</summary>
    public static void RemoveLogo( WriteScope scope, long accountId )
    {
        if ( AttachedOne.Remove( scope, Logo( accountId ), LogoName ) )
        {
            TouchLater( scope, accountId );
        }
    }

    /// <summary><c>SecureRandom.alphanumeric(12).scan(/.{4}/).join("-")</c>.</summary>
    public static string GenerateJoinCode( )
    {
        var code = SecureTokens.Alphanumeric( 12 );
        return $"{code[..4]}-{code[4..8]}-{code[8..]}";
    }

    /// <summary><c>save!</c>: <c>has_json</c>'s <c>before_save</c>, then the changed columns and <c>updated_at</c>.</summary>
    private static void Save( WriteScope scope, Account original, Account account )
    {
        var key = RecordKeys.Account( account.Id );
        scope.Enlist( key );
        account.Settings = AccountSettings.Materialize( account.Settings );

        var changes = new ChangedColumns( TableNames.Accounts );
        changes.Compare( "custom_styles", nameof( Account.CustomStyles ), original.CustomStyles, account.CustomStyles );
        changes.Compare( "join_code", nameof( Account.JoinCode ), original.JoinCode, account.JoinCode );
        changes.Compare( "name", nameof( Account.Name ), original.Name, account.Name );
        changes.Compare( "settings", nameof( Account.Settings ), original.Settings, account.Settings );
        if ( !changes.Any )
        {
            return;
        }

        account.UpdatedAt = scope.Now;
        var rows = DataWrites.Save( ( ) => scope.Db.Connection.Execute( changes.UpdateSql( ), account, scope.Db.Transaction ) );
        DataWrites.ExpectOneRow( rows, key );
    }

    /// <summary>
    /// The account's <c>touch</c>: the logo attachment's <c>belongs_to :record, touch: true</c> (deferred to before
    /// commit by <see cref="TouchLater"/>) and <c>Blob#touch_attachments</c>.
    /// </summary>
    internal static void Touch( CampfireDb db, long accountId, DateTime touchedAt ) =>
        db.Connection.Execute( "UPDATE accounts SET updated_at = @at WHERE id = @accountId", new
        {
            at = touchedAt,
            accountId
        }, db.Transaction );

    private static void TouchLater( WriteScope scope, long accountId )
    {
        var touchedAt = scope.Now;
        scope.BeforeCommit( RecordKeys.Account( accountId ), CommitOrder.Touch, commitScope => Touch( commitScope.Db, accountId, touchedAt ) );
    }

    /// <summary>The holder of the account's logo.</summary>
    private static AttachmentOwner Logo( long accountId ) => new( RecordType, accountId );
}
