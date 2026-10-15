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
        try
        {
            return Hasher.VerifyHashedPassword( User, digest, password );
        }
        catch ( Exception error ) when ( error is FormatException or ArgumentException )
        {
            return PasswordVerificationResult.Failed;
        }
    }

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
