using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Campfire.Web.Hosting;

/// <summary>Reads and writes autocert-compatible PEM files in the existing Thruster certificate directory.</summary>
internal sealed class AcmeCertificateStore( FrontServerOptions options )
{
    private const string RsaCertificateSuffix = "+rsa";

    // Go autocert stores RSA leaf certificates under a separate cache key. Reuse them when there is
    // no default (ECDSA) entry, so replacing the front does not require immediate ACME access.
    public async Task<string?> ReadCertificateAsync( string domain, CancellationToken cancellationToken ) =>
        await ReadAsync( domain, cancellationToken )
        ?? await ReadAsync( domain + RsaCertificateSuffix, cancellationToken );

    public async Task<string?> ReadAsync( string name, CancellationToken cancellationToken )
    {
        var path = Path.Combine( options.StoragePath, name );
        return File.Exists( path ) ? await File.ReadAllTextAsync( path, cancellationToken ) : null;
    }

    public async Task WriteAsync( string name, string pem, CancellationToken cancellationToken )
    {
        Directory.CreateDirectory( options.StoragePath );
        if ( !OperatingSystem.IsWindows( ) )
        {
            File.SetUnixFileMode( options.StoragePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }

        var path = Path.Combine( options.StoragePath, name );
        var temporary = path + "." + Guid.NewGuid( ).ToString( "N" ) + ".tmp";
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if ( !OperatingSystem.IsWindows( ) )
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            await using ( var stream = new FileStream( temporary, fileOptions ) )
            {
                var bytes = Encoding.UTF8.GetBytes( pem );
                try
                {
                    await stream.WriteAsync( bytes, cancellationToken );
                    await stream.FlushAsync( cancellationToken );
                    stream.Flush( flushToDisk: true );
                }
                finally
                {
                    CryptographicOperations.ZeroMemory( bytes );
                }
            }

            File.Move( temporary, path, overwrite: true );
        }
        finally
        {
            if ( File.Exists( temporary ) )
            {
                File.Delete( temporary );
            }
        }
    }

    public static CertificateBundle? Load( string pem, string domain, DateTimeOffset now )
    {
        var certificate = X509Certificate2.CreateFromPem( pem, pem );
        if ( !certificate.HasPrivateKey || !certificate.MatchesHostname( domain, allowWildcards: true, allowCommonName: false )
            || now < certificate.NotBefore.ToUniversalTime( ) || now >= certificate.NotAfter.ToUniversalTime( ) )
        {
            certificate.Dispose( );
            return null;
        }

        var chain = new X509Certificate2Collection( );
        chain.ImportFromPem( pem );
        return new CertificateBundle( certificate, chain );
    }
}
