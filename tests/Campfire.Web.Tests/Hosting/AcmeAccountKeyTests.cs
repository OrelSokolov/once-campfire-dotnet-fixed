using System.Security.Cryptography;
using Campfire.Web.Hosting;

namespace Campfire.Web.Tests.Hosting;

/// <summary>Account keys imported from an existing autocert directory keep their signing identity and JOSE encoding.</summary>
public sealed class AcmeAccountKeyTests
{
    [Fact]
    public void An_existing_rsa_account_signs_rs256_and_reloads_with_the_same_thumbprint( )
    {
        using var rsa = RSA.Create( 2048 );
        using var account = new AcmeAccountKey( rsa.ExportRSAPrivateKeyPem( ) );
        var message = "protected.payload"u8.ToArray( );
        var signature = account.Sign( message );
        Assert.Equal( "RS256", account.Algorithm );
        Assert.True( rsa.VerifyData( message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1 ) );
        message[0] ^= 1;
        Assert.False( rsa.VerifyData( message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1 ) );
        using var reloaded = new AcmeAccountKey( account.ExportPem( ) );
        Assert.Equal( account.Thumbprint, reloaded.Thumbprint );
    }

    [Fact]
    public void An_ec_account_uses_fixed_width_jose_signatures_and_a_stable_persisted_identity( )
    {
        using var key = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        using var account = new AcmeAccountKey( key.ExportECPrivateKeyPem( ) );
        var message = "protected.payload"u8.ToArray( );
        var signature = account.Sign( message );
        Assert.Equal( "ES256", account.Algorithm );
        Assert.Equal( 64, signature.Length );
        Assert.True( key.VerifyData( message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation ) );
        using var reloaded = new AcmeAccountKey( account.ExportPem( ) );
        Assert.Equal( account.Thumbprint, reloaded.Thumbprint );
    }
}
