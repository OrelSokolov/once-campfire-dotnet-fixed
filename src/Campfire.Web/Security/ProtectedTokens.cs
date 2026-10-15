using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Campfire.Web.Security;

/// <summary>Data Protection keys persist alongside the database. No legacy token format is accepted.</summary>
public sealed class ProtectedTokens( IDataProtectionProvider provider ) : ITokenProtector, IDisposable
{
    public const string Namespace = "Campfire.Token.v1";
    public const int MaximumTokenLength = 32 * 1024;
    private const long CacheBytes = 4 * 1024 * 1024;
    private readonly MemoryCache _cache = new( new MemoryCacheOptions { SizeLimit = CacheBytes } );
    private readonly object _creationLock = new( );

    private sealed record TokenKey( string Value, string Purpose, DateTimeOffset? ExpiresAt );

    public string Protect( string value, string purpose, DateTimeOffset? expiresAt = null )
    {
        // Expiry is request-specific; memoizing each instant evicts stable public URLs.
        if ( expiresAt is not null )
        {
            return ProtectEnvelope( value, purpose, expiresAt );
        }

        var key = new TokenKey( value, purpose, expiresAt );
        if ( _cache.TryGetValue( key, out string? cached ) )
        {
            return cached!;
        }

        lock ( _creationLock )
        {
            if ( _cache.TryGetValue( key, out cached ) )
            {
                return cached!;
            }

            var token = ProtectEnvelope( value, purpose, expiresAt );
            _cache.Set( key, token, new MemoryCacheEntryOptions { Size = 2L * (value.Length + purpose.Length + token.Length) } );
            return token;
        }
    }

    private string ProtectEnvelope( string value, string purpose, DateTimeOffset? expiresAt ) =>
        Protector( purpose ).Protect( new JsonObject { ["value"] = value, ["expires"] = expiresAt?.ToUnixTimeMilliseconds( ) }.ToJsonString( ) );

    public string? Unprotect( string token, string purpose, DateTimeOffset now ) => Unprotect( token, purpose, now, out _ );

    public string? Unprotect( string token, string purpose, DateTimeOffset now, out DateTimeOffset? expiresAt )
    {
        expiresAt = null;
        if ( token.Length > MaximumTokenLength )
        {
            return null;
        }

        try
        {
            var envelope = JsonNode.Parse( Protector( purpose ).Unprotect( token ) ) as JsonObject;
            if ( envelope?["expires"] is { } expiry )
            {
                if ( !long.TryParse( expiry.ToJsonString( ), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds )
                    || now.ToUnixTimeMilliseconds( ) >= milliseconds )
                {
                    return null;
                }

                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds( milliseconds );
            }

            return envelope?["value"]?.GetValue<string>( );
        }
        catch ( Exception error ) when ( error is CryptographicException or JsonException or InvalidOperationException or FormatException
            or ArgumentOutOfRangeException )
        {
            expiresAt = null;
            // Malformed or tampered tokens are a failed lookup, never a server error.
            return null;
        }
    }

    private IDataProtector Protector( string purpose ) => _cache.GetOrCreate( purpose, entry =>
    {
        entry.Size = 512 + purpose.Length * 2L;
        return provider.CreateProtector( Namespace, purpose );
    } )!;

    public void Dispose( ) => _cache.Dispose( );
}
