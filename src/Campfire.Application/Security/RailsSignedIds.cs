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

    /// <summary>The on-wire purpose of a Rails signed id: the underscored model name, a slash, then the purpose.</summary>
    public static string? Purpose( string modelName, string? purpose )
    {
        var snake = new StringBuilder( modelName.Length + 4 );
        foreach ( var ch in modelName )
        {
            _ = char.IsUpper( ch ) ? snake.Append( snake.Length > 0 ? "_" : "" ).Append( char.ToLowerInvariant( ch ) ) : snake.Append( ch );
        }

        return purpose is null ? snake.ToString( ) : $"{snake}/{purpose}";
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
        return DigestMatches( key, hash, data, digest ) ? EnvelopeId( data, purpose, now ) : null;
    }

    private static bool DigestMatches( byte[] key, HashAlgorithmName hash, string data, string digest )
    {
        var expected = Convert.ToHexString( Hmac( hash, key, Encoding.UTF8.GetBytes( data ) ) ).ToLower( CultureInfo.InvariantCulture );
        var provided = Encoding.UTF8.GetBytes( digest.ToLower( CultureInfo.InvariantCulture ) );
        return CryptographicOperations.FixedTimeEquals( Encoding.UTF8.GetBytes( expected ), provided );
    }

    private static long? EnvelopeId( string data, string? purpose, DateTimeOffset now )
    {
        try
        {
            using var document = JsonDocument.Parse( Decode( data ) );
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty( "_rails", out var envelope )
                && envelope.ValueKind == JsonValueKind.Object
                ? ReadEnvelope( envelope, purpose, now )
                : null;
        }
        catch ( Exception error ) when ( error is FormatException or ArgumentException or JsonException )
        {
            return null;
        }
    }

    private static long? ReadEnvelope( JsonElement envelope, string? purpose, DateTimeOffset now ) =>
        PurposeMatches( envelope, purpose ) && !Expired( envelope, now ) ? PayloadId( envelope ) : null;

    private static bool PurposeMatches( JsonElement envelope, string? purpose ) =>
        !envelope.TryGetProperty( "pur", out var declared ) || declared.ValueKind != JsonValueKind.String || declared.GetString( ) == purpose;

    private static bool Expired( JsonElement envelope, DateTimeOffset now ) =>
        envelope.TryGetProperty( "exp", out var expiry ) && expiry.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse( expiry.GetString( ), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt )
        && now >= expiresAt;

    private static long? PayloadId( JsonElement envelope )
    {
        if ( envelope.TryGetProperty( "data", out var plain ) )
        {
            return AsId( plain );
        }

        // The standalone-gem envelope keeps the message in strict base64 of the JSON payload.
        return envelope.TryGetProperty( "message", out var wrapped ) && wrapped.ValueKind == JsonValueKind.String
            ? AsDecodedId( wrapped.GetString( )! )
            : null;
    }

    private static long? AsDecodedId( string encoded )
    {
        try
        {
            using var inner = JsonDocument.Parse( Decode( encoded ) );
            return AsId( inner.RootElement );
        }
        catch ( Exception error ) when ( error is FormatException or ArgumentException or JsonException )
        {
            return null;
        }
    }

    private static long? AsId( JsonElement element ) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt64( out var id ) ? id : null;

    /// <summary>Base64 in either alphabet, like <c>MessageVerifier#decode</c>, which retries with the other one.</summary>
    private static byte[] Decode( string encoded )
    {
        var normalized = encoded.Replace( '-', '+' ).Replace( '_', '/' );
        return Convert.FromBase64String( normalized.PadRight( normalized.Length + (4 - normalized.Length % 4) % 4, '=' ) );
    }

    private static byte[] Hmac( HashAlgorithmName hash, byte[] key, byte[] data )
    {
#pragma warning disable CA5350 // Rails' pre-7.1 signed_id digests are HMAC-SHA1; verification must match the format.
        return hash == HashAlgorithmName.SHA256 ? HMACSHA256.HashData( key, data ) : HMACSHA1.HashData( key, data );
#pragma warning restore CA5350
    }
}
