using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Campfire.Application.Integrations.Network;

namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>The test-only certificates in <c>vectors/integrations/tls/</c> and the vector files.</summary>
internal static class IntegrationVectors
{
    public static string Path( params string[] segments ) => RepositoryPaths.VectorPath( ["integrations", .. segments] );

    public static string Text( params string[] segments ) => File.ReadAllText( Path( segments ) );

    /// <summary>The test CA that signed the server certificate.</summary>
    public static X509Certificate2Collection TestRoots =>
        [X509CertificateLoader.LoadCertificate( X509Certificate2.CreateFromPem( Text( "tls", "ca.pem" ) ).RawData )];

    /// <summary>The server certificate (for fcm.googleapis.com, www.example.com, example.com, bots.example) with its key.</summary>
    public static X509Certificate2 ServerCertificate( )
    {
        using var pem = X509Certificate2.CreateFromPemFile( Path( "tls", "server.pem" ), Path( "tls", "server.key" ) );
        return X509CertificateLoader.LoadPkcs12( pem.Export( X509ContentType.Pkcs12 ), null );
    }

    /// <summary>A self-signed certificate for <paramref name="host"/>, generated for one test, with its key.</summary>
    public static X509Certificate2 SelfSigned( string host )
    {
        using var key = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        var request = new CertificateRequest( $"CN={host}", key, HashAlgorithmName.SHA256 );
        var names = new SubjectAlternativeNameBuilder( );
        names.AddDnsName( host );
        request.CertificateExtensions.Add( names.Build( ) );
        request.CertificateExtensions.Add( new X509BasicConstraintsExtension( certificateAuthority: true, false, 0, critical: true ) );
        using var certificate = request.CreateSelfSigned( DateTimeOffset.UtcNow.AddDays( -1 ), DateTimeOffset.UtcNow.AddDays( 1 ) );
        return X509CertificateLoader.LoadPkcs12( certificate.Export( X509ContentType.Pkcs12 ), null );
    }

    /// <summary>A network whose resolver and dialer are fakes and whose TLS trusts only the test CA.</summary>
    public static OutboundNetwork Network( IHostResolver resolver, ISocketDialer dialer ) => new( resolver, dialer, TestRoots );
}
