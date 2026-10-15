namespace Campfire.Application.Security;

/// <summary>Purpose-bound authenticated tokens. The host owns key storage and cryptographic implementation.</summary>
public interface ITokenProtector
{
    string Protect( string value, string purpose, DateTimeOffset? expiresAt = null );
    string? Unprotect( string token, string purpose, DateTimeOffset now );
    string? Unprotect( string token, string purpose, DateTimeOffset now, out DateTimeOffset? expiresAt );
}
