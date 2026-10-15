using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Campfire.Web.Hosting;

/// <summary>A leaf certificate with its chain, ready for TLS handshakes; disposing releases every certificate.</summary>
internal sealed class CertificateBundle( X509Certificate2 certificate, X509Certificate2Collection chain ) : IDisposable
{
    internal DateTimeOffset Expires { get; } = certificate.NotAfter.ToUniversalTime( );
    public X509Certificate2 Certificate { get; } = certificate;
    public SslStreamCertificateContext Context { get; } = SslStreamCertificateContext.Create( certificate, chain, offline: true );

    public void Dispose( )
    {
        Certificate.Dispose( );
        foreach ( var item in chain )
        {
            item.Dispose( );
        }
    }
}
