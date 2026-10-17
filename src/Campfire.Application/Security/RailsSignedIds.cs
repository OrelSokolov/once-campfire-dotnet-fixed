using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Campfire.Application.Security;

/// <summary>
/// Verifies <c>ActiveRecord::SignedId</c> tokens minted by a Rails installation that owns the same database:
/// <c>Base64(json) + "--" + hexdigest(HMAC)</c> in the Rails metadata envelope, keyed by
/// <c>ActiveSupport::KeyGenerator</c> (PBKDF2-HMAC-SHA1, 1000 iterations) over <c>SECRET_KEY_BASE</c> with the
/// <c>active_record/signed_id</c> salt. SHA-256 digests first (the Rails 7.1+ default), SHA-1 as the fallback.
/// Native Data Protection tokens remain authoritative; this reads only tokens Rails signed before a migration.
/// </summary>
public static class RailsSignedIds
{
    public const string SecretVariable = "SECRET_KEY_BASE";
    public const string Salt = "active_record/signed_id";

    private static byte[]? _key;

    /// <summary>The <paramref name="signedId"/>'s record id, or null when it is forged, expired, or for another purpose.</summary>
    public static long? Verify( string signedId, string? purpose, DateTimeOffset now )
    {
        ArgumentNullException.ThrowIfNull( signedId );

        if ( Key( ) is not { } key )
        {
            return null;
        }

        return Verify( key, signedId, purpose, now, HashAlgorithmName.SHA256, 64 )
            ?? Verify( key, signedId, purpose, now, HashAlgorithmName.SHA1, 40 );
    }

    /// <summary>Derives the verifier key once per process; without <see cref="SecretVariable"/> every token is native.</summary>
    public static byte[]? Key( )
    {
        if ( _key is { } cached )
        {
            return cached;
        }

        var secret = Environment.GetEnvironmentVariable( SecretVariable );
        if ( string.IsNullOrEmpty( secret ) )
        {
            return null;
        }

        _key = Rfc2898DeriveBytes.Pbkdf2( Encoding.UTF8.GetBytes( secret ), Encoding.UTF8.GetBytes( Salt ), 1000, HashAlgorithmName.SHA1, 64 );
        return _key;
    }

    private static long? Verify( byte[] key, string signedId, string? purpose, DateTimeOffset now, HashAlgorithmName hash, int digestCharacters )
    {
        if ( signedId.Length <= digestCharacters + 2 )
        {
            return null;
        }

        var separator = signedId.Length - digestCharacters - 2;
        if ( signedId.AsSpan( separator, 2 ) is not "--" )
        {
            return null;
        }

        var data = signedId[..separator];
        var digest = signedId[(separator + 2)..];
        var expected = Convert.ToHexString( HMAC.HashData( hash, key, Encoding.UTF8.GetBytes( data ) ) ).ToLowerInvariant( CultureInfo.InvariantCulture );
        if ( !CryptographicOperations.FixedTimeEquals( Encoding.UTF8.GetBytes( expected ), Encoding.UTF8.GetBytes( digest.ToLowerInvariant( CultureInfo.InvariantCulture ) ) ) )
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse( Decode( data ) );
            if ( document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty( "_rails", out var envelope )
                || envelope.ValueKind != JsonValueKind.Object )
            {
                return null;
            }

            if ( envelope.TryGetProperty( "pur", out var declared ) && declared.ValueKind == JsonValueKind.String && declared.GetString( ) != purpose )
            {
                return null;
            }

            if ( envelope.TryGetProperty( "exp", out var expiry ) && expiry.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse( expiry.GetString( ), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt )
                && now >= expiresAt )
            {
                return null;
            }

            if ( envelope.TryGetProperty( "data", out var plain ) )
            {
                return plain.ValueKind == JsonValueKind.Number && plain.TryGetInt64( out var id ) ? id : null;
            }

            // The standalone-gem envelope keeps the message in strict base64 of the JSON payload.
            if ( envelope.TryGetProperty( "message", out var wrapped ) && wrapped.ValueKind == JsonValueKind.String )
            {
                using var inner = JsonDocument.Parse( Decode( wrapped.GetString( )! ) );
                return inner.RootElement.ValueKind == JsonValueKind.Number && inner.RootElement.TryGetInt64( out var id ) ? id : null;
            }

            return null;
        }
        catch ( Exception error ) when ( error is FormatException or ArgumentException or JsonException )
        {
            return null;
        }
    }

    /// <summary>Base64 in either alphabet, like <c>MessageVerifier#decode</c>, which retries with the other one.</summary>
    private static byte[] Decode( string encoded )
    {
        var normalized = encoded.Replace( '-', '+' ).Replace( '_', '/' );
        return Convert.FromBase64String( normalized.PadRight( normalized.Length + (4 - normalized.Length % 4) % 4, '=' ) );
    }
}
