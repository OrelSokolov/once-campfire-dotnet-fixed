using System.Collections.Concurrent;
using Campfire.Application.Security;

namespace Campfire.Application.Tests.Storage.Support;

/// <summary>Opaque issued tokens for application tests. Cryptography and key persistence are tested in the web host.</summary>
public sealed class TestTokenProtector : ITokenProtector
{
    private readonly ConcurrentDictionary<string, Token> _issued = new( StringComparer.Ordinal );

    public string Protect( string value, string purpose, DateTimeOffset? expiresAt = null )
    {
        var token = Guid.NewGuid( ).ToString( "N" );
        _issued[token] = new Token( value, purpose, expiresAt );
        return token;
    }

    public string? Unprotect( string token, string purpose, DateTimeOffset now ) =>
        _issued.TryGetValue( token, out var issued ) && issued.Purpose == purpose &&
        (issued.ExpiresAt is null || now < issued.ExpiresAt) ? issued.Value : null;

    public string? Unprotect( string token, string purpose, DateTimeOffset now, out DateTimeOffset? expiresAt )
    {
        var value = Unprotect( token, purpose, now );
        expiresAt = value is not null ? _issued[token].ExpiresAt : null;
        return value;
    }

    private sealed record Token( string Value, string Purpose, DateTimeOffset? ExpiresAt );
}
