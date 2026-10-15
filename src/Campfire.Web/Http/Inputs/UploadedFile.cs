namespace Campfire.Web.Http.Inputs;

/// <summary>Own a staged upload path until attachment processing has copied it into storage.</summary>
public sealed class UploadedFile(
    string originalFilename,
    string? contentType,
    string headers,
    string path,
    bool ownsFile = true,
    IFormFile? formFile = null ) : IDisposable
{
    private const int ReadBufferBytes = 81920;


    public string OriginalFilename { get; } = System.IO.Path.GetFileName( originalFilename.Replace( '\\', '/' ) );
    public string? ContentType { get; } = contentType;
    public string Headers { get; } = headers;
    public string Path { get; } = path;
    public long Length => formFile?.Length ?? new FileInfo( Path ).Length;
    public Stream OpenRead( ) =>
        formFile?.OpenReadStream( ) ?? new FileStream( Path, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferBytes, FileOptions.Asynchronous );

    public static Task<UploadedFile> FromFormFileAsync( IFormFile file, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        // ASP.NET owns the multipart spool. Read that stream directly when staging the attachment.
        return Task.FromResult( new UploadedFile( file.FileName, file.ContentType, "", "", ownsFile: false, formFile: file ) );
    }

    public static UploadedFile FromBytes( string originalFilename, string? contentType, ReadOnlySpan<byte> bytes, string? directory = null )
    {
        var path = CreateTempFile( directory );
        try
        {
            using var output = new FileStream( path, FileMode.Truncate, FileAccess.Write );
            output.Write( bytes );
            return new UploadedFile( originalFilename, contentType, "", path );
        }
        catch
        {
            File.Delete( path );
            throw;
        }
    }

    internal static string CreateTempFile( string? directory )
    {
        var path = System.IO.Path.Combine( directory ?? System.IO.Path.GetTempPath( ), "CampfireUpload" + Guid.NewGuid( ).ToString( "N" ) );
        using var output = new FileStream( path, FileMode.CreateNew, FileAccess.Write );
        return path;
    }

    public void Dispose( )
    {
        if ( ownsFile )
        {
            File.Delete( Path );
        }
    }
}
