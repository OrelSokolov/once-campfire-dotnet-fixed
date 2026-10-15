namespace Campfire.Application.Accounts;

/// <summary>A row of <c>accounts</c> (<c>app/models/account.rb</c>). There is at most one.</summary>
public sealed class Account
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary><c>Account::Joinable</c>: three dash-separated groups of four alphanumerics.</summary>
    public string JoinCode { get; set; } = "";

    public string? CustomStyles { get; set; }

    /// <summary>
    /// The raw <c>settings</c> JSON column (<c>has_json :settings</c>). Read and change it through
    /// <see cref="AccountSettings"/>.
    /// </summary>
    public string? Settings { get; set; }

    /// <summary>Always 0; a unique index on it keeps the table to one row.</summary>
    public long SingletonGuard { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>A copy of the row as read, to find the columns a save changes (<see cref="Data.ChangedColumns"/>).</summary>
    internal Account Copy( ) => ( Account ) MemberwiseClone( );
}
