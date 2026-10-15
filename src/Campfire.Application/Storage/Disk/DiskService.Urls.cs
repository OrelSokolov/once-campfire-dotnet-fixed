using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;
using Campfire.Application.Storage.Http;

namespace Campfire.Application.Storage.Disk;

/// <summary>
/// The disk service's signed URLs: <c>/rails/active_storage/disk/:encoded_key/*filename</c> and
/// <c>PUT /rails/active_storage/disk/:encoded_token</c>.
/// </summary>
public sealed partial class DiskService
{
    public const string UrlPrefix = "/rails/active_storage/disk";

    /// <summary><c>ActiveStorage.service_urls_expire_in</c>: how long a redirect's disk URL stays valid.</summary>
    public static readonly TimeSpan ServiceUrlsExpireIn = TimeSpan.FromMinutes( 5 );

    /// <summary>
    /// The path of <c>service.url(key, expires_in:, filename:, content_type:, disposition:)</c> (the
    /// caller adds <c>ActiveStorage::Current.url_options</c>' protocol and host). The signed payload is
    /// <c>{key, disposition, content_type, service_name}</c>, in that order.
    /// </summary>
    public string UrlPath(
        ActiveStorageVerifier verifier, string key, DateTimeOffset? expiresAt, BlobFilename filename, string? contentType, string? disposition )
    {
        ArgumentNullException.ThrowIfNull( verifier );
        ArgumentNullException.ThrowIfNull( filename );

        var sanitized = filename.Sanitized;
        var payload = new JsonObject
        {
            [PayloadKeys.Key] = key,
            [PayloadKeys.Disposition] = ContentDisposition.With( disposition, sanitized ),
            [PayloadKeys.ContentType] = contentType,
            [PayloadKeys.ServiceName] = Name,
        };
        var encodedKey = verifier.Generate( JsonPayload.Encode( payload ), ActiveStorageVerifier.BlobKeyPurpose, expiresAt );

        return $"{UrlPrefix}/{ContentDisposition.EscapeSegment( encodedKey )}/{ContentDisposition.EscapePath( sanitized )}";
    }

    /// <summary>
    /// The path of <c>url_for_direct_upload(key, expires_in:, content_type:, content_length:, checksum:)</c>;
    /// the signed payload is <c>{key, content_type, content_length, checksum, service_name}</c>.
    /// </summary>
    public string UrlPathForDirectUpload(
        ActiveStorageVerifier verifier, string key, DateTimeOffset expiresAt, string? contentType, long contentLength, string checksum )
    {
        ArgumentNullException.ThrowIfNull( verifier );

        var payload = new JsonObject
        {
            [PayloadKeys.Key] = key,
            [PayloadKeys.ContentType] = contentType,
            [PayloadKeys.ContentLength] = contentLength,
            [PayloadKeys.Checksum] = checksum,
            [PayloadKeys.ServiceName] = Name,
        };
        var token = verifier.Generate( JsonPayload.Encode( payload ), ActiveStorageVerifier.BlobTokenPurpose, expiresAt );

        return $"{UrlPrefix}/{ContentDisposition.EscapeSegment( token )}";
    }

    /// <summary><c>DiskController#decode_verified_key</c>: <c>null</c> for a bad, expired or malformed key (the controller answers 404).</summary>
    public static DiskKey? DecodeVerifiedKey( ActiveStorageVerifier verifier, string encodedKey, DateTimeOffset now )
    {
        ArgumentNullException.ThrowIfNull( verifier );

        if ( verifier.VerifiedNode( encodedKey, ActiveStorageVerifier.BlobKeyPurpose, now, out var valid ) is not JsonObject data || !valid )
        {
            return null;
        }

        var key = Text( data, PayloadKeys.Key );
        var disposition = Text( data, PayloadKeys.Disposition );
        var serviceName = Text( data, PayloadKeys.ServiceName );
        if ( key is null || disposition is null || serviceName is null )
        {
            return null;
        }

        return new DiskKey( key, disposition, Text( data, PayloadKeys.ContentType ), serviceName );
    }

    /// <summary><c>DiskController#decode_verified_token</c>: <c>null</c> for a bad, expired or malformed token (404).</summary>
    public static DiskToken? DecodeVerifiedToken( ActiveStorageVerifier verifier, string encodedToken, DateTimeOffset now )
    {
        ArgumentNullException.ThrowIfNull( verifier );

        if ( verifier.VerifiedNode( encodedToken, ActiveStorageVerifier.BlobTokenPurpose, now, out var valid ) is not JsonObject data || !valid )
        {
            return null;
        }

        var key = Text( data, PayloadKeys.Key );
        var checksum = Text( data, PayloadKeys.Checksum );
        var serviceName = Text( data, PayloadKeys.ServiceName );
        var length = WholeNumber( data, PayloadKeys.ContentLength );
        if ( key is null || checksum is null || serviceName is null || length is null )
        {
            return null;
        }

        return new DiskToken( key, Text( data, PayloadKeys.ContentType ), length.Value, checksum, serviceName );
    }

    private static string? Text( JsonObject data, string name ) =>
        data[name] is JsonValue value && value.GetValueKind( ) == JsonValueKind.String ? value.GetValue<string>( ) : null;

    private static long? WholeNumber( JsonObject data, string name )
    {
        if ( data[name] is JsonValue value && value.GetValueKind( ) == JsonValueKind.Number && long.TryParse( value.ToJsonString( ), out var parsed ) )
        {
            return parsed;
        }

        return null;
    }

    /// <summary>The keys of the signed URL payloads, as Rails' disk service writes them.</summary>
    private static class PayloadKeys
    {
        public const string Key = "key";
        public const string Disposition = "disposition";
        public const string ContentType = "content_type";
        public const string ContentLength = "content_length";
        public const string Checksum = "checksum";
        public const string ServiceName = "service_name";
    }
}
