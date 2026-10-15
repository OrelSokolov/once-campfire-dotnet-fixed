using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Security;
using Campfire.Application.Serialization;

namespace Campfire.Application.Storage;

/// <summary>
/// <c>ActiveStorage.verifier</c> (<c>Rails.application.message_verifier("ActiveStorage")</c>). Active
/// Storage signs blob ids (purpose <c>blob_id</c>: <c>ActiveStorage::Blob</c> overrides both the
/// signed-id verifier and <c>combine_signed_id_purposes</c>, so this is not the Active Record
/// signed-id scheme), variation keys (<c>variation</c>), disk URLs (<c>blob_key</c>) and direct-upload
/// tokens (<c>blob_token</c>). Data travels as JSON the caller encodes, because key order is part of
/// the signed bytes.
/// </summary>
public sealed class ActiveStorageVerifier
{
    public const string BlobIdPurpose = "blob_id";
    public const string VariationPurpose = "variation";
    public const string BlobKeyPurpose = "blob_key";
    public const string BlobTokenPurpose = "blob_token";

    private readonly ITokenProtector _verifier;

    public ActiveStorageVerifier( ITokenProtector verifier )
    {
        ArgumentNullException.ThrowIfNull( verifier );

        _verifier = verifier;
    }

    /// <summary><c>generate(data, purpose:, expires_at:)</c> with <paramref name="dataJson"/> already encoded.</summary>
    public string Generate( string dataJson, string purpose, DateTimeOffset? expiresAt = null ) =>
        _verifier.Protect( dataJson, "storage/" + purpose, expiresAt );

    /// <summary><c>verified(message, purpose:)</c>: the data's JSON when the message is authentic, unexpired and on purpose.</summary>
    public string? Verified( string message, string purpose, DateTimeOffset now )
    {
        ArgumentNullException.ThrowIfNull( message );

        return _verifier.Unprotect( message, "storage/" + purpose, now );
    }

    /// <summary><see cref="Verified"/>, parsed.</summary>
    public JsonNode? VerifiedNode( string message, string purpose, DateTimeOffset now, out bool valid )
    {
        var json = Verified( message, purpose, now );
        valid = json is not null;

        return json is null ? null : JsonPayload.Parse( json );
    }

    /// <summary><c>blob.signed_id(expires_at:)</c>. Campfire leaves <c>urls_expire_in</c> unset, so route URLs pass no expiry.</summary>
    public string SignedBlobId( long blobId, DateTimeOffset? expiresAt = null ) =>
        Generate( blobId.ToString( CultureInfo.InvariantCulture ), BlobIdPurpose, expiresAt );

    /// <summary>
    /// The verification half of <c>ActiveStorage::Blob.find_signed(signed_id)</c>: the id to look up
    /// (<c>find_by(id:)</c> also casts a numeric string), or <c>null</c>.
    /// </summary>
    public long? VerifySignedBlobId( string signedId, DateTimeOffset now )
    {
        var data = VerifiedNode( signedId, BlobIdPurpose, now, out var valid );
        if ( !valid || data is not JsonValue scalar )
        {
            return null;
        }

        var text = scalar.GetValueKind( ) switch
        {
            JsonValueKind.Number => scalar.ToJsonString( ),
            JsonValueKind.String => scalar.GetValue<string>( ).Trim( ),
            _ => null,
        };
        return long.TryParse( text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id ) ? id : null;
    }
}
