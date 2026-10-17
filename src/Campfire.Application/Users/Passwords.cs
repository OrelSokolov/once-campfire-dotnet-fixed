using BCrypt.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Campfire.Application.Users;

/// <summary>Identity V3 PBKDF2 hashes. Password work runs outside database leases.</summary>
public static class Passwords
{
    public const int DefaultIterations = 100_000;
    public const int MinimumIterations = 1_000;
    private static readonly object User = new( );
    private static readonly PasswordHasher<object> Hasher = Create( DefaultIterations );
    private static readonly string DummyDigest = Hasher.HashPassword( User, "unused-password-for-equal-work" );

    public static string Digest( string password, int iterations = DefaultIterations ) => Create( iterations ).HashPassword( User, password );

    public static bool Verify( string password, string? digest )
    {
        if ( string.IsNullOrEmpty( digest ) )
        {
            return false;
        }

        if ( IsBCryptDigest( digest ) )
        {
            // A digest minted by a Rails installation (bcrypt, "$2..." formats): verified here, then upgraded
            // to Identity V3 by VerifyResult's SuccessRehashNeeded path on the next successful login.
            try
            {
                return BCrypt.Verify( password, digest );
            }
            catch ( SaltParseException )
            {
                return false;
            }
        }

        try
        {
            return VerifyResult( password, digest ) != PasswordVerificationResult.Failed;
        }
        catch ( Exception error ) when ( error is FormatException or ArgumentException )
        {
            return false;
        }
    }
    public static void SpendEqualTime( string password ) => Verify( password, DummyDigest );

    public static PasswordVerificationResult VerifyResult( string password, string digest )
    {
        if ( IsBCryptDigest( digest ) )
        {
            // Rails compatibility: a verified bcrypt digest always rehashes to Identity V3.
            try
            {
                return BCrypt.Verify( password, digest )
                    ? PasswordVerificationResult.SuccessRehashNeeded
                    : PasswordVerificationResult.Failed;
            }
            catch ( SaltParseException )
            {
                return PasswordVerificationResult.Failed;
            }
        }

        try
        {
            return Hasher.VerifyHashedPassword( User, digest, password );
        }
        catch ( Exception error ) when ( error is FormatException or ArgumentException )
        {
            return PasswordVerificationResult.Failed;
        }
    }

    /// <summary>Rails' <c>has_secure_password</c> digests: bcrypt's <c>$2</c>, <c>$2a</c>, <c>$2b</c> and <c>$2y</c>.</summary>
    private static bool IsBCryptDigest( string digest ) =>
        digest.Length >= 4 && digest[0] == '$' && digest[1] == '2' && (digest[2] == '$' || digest[3] == '$');

    private static PasswordHasher<object> Create( int iterations )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan( iterations, MinimumIterations );
        return new PasswordHasher<object>( Options.Create( new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = iterations
        } ) );
    }
}
