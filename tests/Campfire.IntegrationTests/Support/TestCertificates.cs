using System.Security.Cryptography.X509Certificates;

namespace Campfire.IntegrationTests.Support;

/// <summary>The test-only certificates in <c>vectors/integrations/tls/</c>.</summary>
internal static class TestCertificates
{
    /// <summary>The test CA that signed the server certificate.</summary>
    public static X509Certificate2 Authority( ) =>
        X509CertificateLoader.LoadCertificate( X509Certificate2.CreateFromPem( File.ReadAllText( TlsFile( "ca.pem" ) ) ).RawData );

    /// <summary>The server certificate (fcm.googleapis.com, www.example.com, example.com, bots.example) with its key.</summary>
    public static X509Certificate2 Server( )
    {
        using var pem = X509Certificate2.CreateFromPemFile( TlsFile( "server.pem" ), TlsFile( "server.key" ) );
        return X509CertificateLoader.LoadPkcs12( pem.Export( X509ContentType.Pkcs12 ), null );
    }

    private static string TlsFile( string file ) => RepositoryPaths.VectorPath( "integrations", "tls", file );
}
