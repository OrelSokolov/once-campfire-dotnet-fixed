using System.Globalization;
using System.Text;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Http;
using Campfire.Application.Tests.Storage.Support;

namespace Campfire.Application.Tests.Storage;

/// <summary><c>ActiveStorage::Streaming#send_blob_byte_range_data</c> and <c>send_blob_stream</c>'s headers (the blob proxy).</summary>
public sealed class BlobStreamingTests : IDisposable
{
    private readonly StorageHarness _harness = new( );

    public void Dispose( ) => _harness.Dispose( );

    [Theory]
    [InlineData( null )]
    [InlineData( "" )]
    [InlineData( "bytes=4-1" )]
    [InlineData( "bytes=999999-" )]
    [InlineData( "items=0-1" )]
    public async Task No_usable_range_is_range_not_satisfiable( string? range )
    {
        var blob = await UploadAsync( "0123456789", "notes.txt", "text/plain" );

        var served = BlobStreaming.ByteRangeData( _harness.Storage.Disk, blob, range );

        Assert.Equal( 416, served.Status );
        Assert.Empty( served.Headers );
        Assert.Empty( served.Body );
    }

    [Fact]
    public async Task One_range_is_sent_inline_with_content_range_and_binary_transfer_encoding( )
    {
        var blob = await UploadAsync( "0123456789", "notes.txt", "text/plain" );

        var served = BlobStreaming.ByteRangeData( _harness.Storage.Disk, blob, "bytes=2-4" );

        Assert.Equal( 206, served.Status );
        Assert.Equal(
            [
                "content-range: bytes 2-4/10", "accept-ranges: bytes", "content-length: 3", "content-type: text/plain",
                "content-disposition: attachment; filename=\"notes.txt\"; filename*=UTF-8''notes.txt", "content-transfer-encoding: binary",
            ],
            served.Headers.Select( header => $"{header.Key}: {header.Value}" ) );
        Assert.Equal( "234", Body( served ) );
    }

    [Fact]
    public async Task Several_ranges_are_multipart_with_capitalized_part_headers( )
    {
        var blob = await UploadAsync( "0123456789", "a.png", "image/png" );

        var served = BlobStreaming.ByteRangeData( _harness.Storage.Disk, blob, "bytes=0-1,3-4", boundary: "b0undary" );

        Assert.Equal( 206, served.Status );
        Assert.Equal( "multipart/byteranges; boundary=b0undary", served.Header( "content-type" ) );
        Assert.Equal( "inline; filename=\"a.png\"; filename*=UTF-8''a.png", served.Header( "content-disposition" ) );
        var body = Body( served );
        Assert.Equal(
            "\r\n--b0undary\r\nContent-Type: image/png\r\nContent-Range: bytes 0-1/10\r\n\r\n01" +
            "\r\n--b0undary\r\nContent-Type: image/png\r\nContent-Range: bytes 3-4/10\r\n\r\n34\r\n--b0undary--\r\n",
            body );
        Assert.Equal( body.Length.ToString( CultureInfo.InvariantCulture ), served.Header( "content-length" ) );
        Assert.Null( served.Header( "content-range" ) );
    }

    [Fact]
    public async Task A_random_boundary_is_32_hex_digits( )
    {
        var blob = await UploadAsync( "0123456789", "a.png", "image/png" );

        var type = BlobStreaming.ByteRangeData( _harness.Storage.Disk, blob, "bytes=0-1,3-4" ).Header( "content-type" )!;

        Assert.Matches( "^multipart/byteranges; boundary=[0-9a-f]{32}$", type );
    }

    [Fact]
    public void Streams_use_the_serving_type_and_the_forced_or_requested_disposition( )
    {
        var html = new Blob { Id = 1, Key = "k", Filename = new BlobFilename( "x.html" ), ServiceName = "local", ContentType = "text/html" };
        var png = html with
        {
            Filename = new BlobFilename( "x.png" ),
            ContentType = "image/png"
        };

        Assert.Equal(
            ["application/octet-stream", "attachment; filename=\"x.html\"; filename*=UTF-8''x.html"],
            BlobStreaming.StreamHeaders( html, "inline" ).Select( header => header.Value ) );
        Assert.Equal( "inline; filename=\"x.png\"; filename*=UTF-8''x.png", BlobStreaming.StreamHeaders( png, null )[1].Value );
        Assert.Equal( "attachment; filename=\"x.png\"; filename*=UTF-8''x.png", BlobStreaming.StreamHeaders( png, "attachment" )[1].Value );
    }

    private async Task<Blob> UploadAsync( string content, string filename, string contentType )
    {
        using var stream = new MemoryStream( Encoding.ASCII.GetBytes( content ) );

        return await _harness.Storage.CreateAndUploadAsync( stream, new BlobFilename( filename ), contentType, TestContext.Current.CancellationToken );
    }

    private static string Body( ServedFile served ) => string.Concat( served.Body.Select( part => part switch
    {
        BytesPart bytes => Encoding.ASCII.GetString( bytes.Bytes ),
        FilePart file => File.ReadAllText( file.Path ).Substring( ( int ) file.Offset, ( int ) file.Length ),
        _ => throw new InvalidOperationException( ),
    } ) );
}
