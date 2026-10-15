using System.Globalization;

using Campfire.Application.Data;

namespace Campfire.Application.Security;

/// <summary>
/// <c>ActiveRecord::SignedId</c>: a record's id signed for one model and purpose (<c>signed_id</c>), and verified back
/// (<c>find_signed</c>), optionally with an expiry.
/// </summary>
public sealed class SignedIds( ITokenProtector tokens )
{
    public const string Namespace = "record-id";

    public const string TransferPurpose = "transfer";

    public const string AvatarPurpose = "avatar";

    public static readonly TimeSpan TransferLifetime = TimeSpan.FromHours( 4 );

    /// <summary><c>signed_id(purpose:, expires_at:)</c> of a <paramref name="modelName"/> record.</summary>
    public string Generate( string modelName, long id, string? purpose = null, DateTimeOffset? expiresAt = null ) =>
        tokens.Protect( id.ToString( CultureInfo.InvariantCulture ), CombinePurposes( modelName, purpose ), expiresAt );

    /// <summary><c>user.transfer_id</c>: a signed user id that expires after <see cref="TransferLifetime"/>.</summary>
    public string GenerateTransferId( long userId, DateTimeOffset now ) =>
        Generate( ModelNames.User, userId, TransferPurpose, now + TransferLifetime );

    /// <summary>The id <paramref name="signedId"/> carries, or null when it is forged, expired or for another model or purpose.</summary>
    public long? Verify( string modelName, string signedId, string? purpose, DateTimeOffset now ) =>
        Verify( modelName, signedId, purpose, now, out _ );

    /// <inheritdoc cref="Verify(string, string, string?, DateTimeOffset)"/>
    /// <param name="modelName">The record's model.</param>
    /// <param name="signedId">The signed id.</param>
    /// <param name="purpose">The purpose it was signed for.</param>
    /// <param name="now">The time to check the expiry against.</param>
    /// <param name="expiresAt">When the signed id expires; null when it never does or does not verify.</param>
    public long? Verify( string modelName, string signedId, string? purpose, DateTimeOffset now, out DateTimeOffset? expiresAt )
    {
        var value = tokens.Unprotect( signedId, CombinePurposes( modelName, purpose ), now, out expiresAt );
        return long.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out var id ) ? id : null;
    }

    /// <summary>The token purpose of a signed id: the namespace, the model and the purpose.</summary>
    public static string CombinePurposes( string modelName, string? purpose ) => $"{Namespace}/{modelName}/{purpose}";
}
