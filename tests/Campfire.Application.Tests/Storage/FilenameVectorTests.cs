using System.Text;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Http;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary>
/// <c>ActiveStorage::Filename</c> (sanitizing, base and extension), <c>ContentDisposition.format</c>
/// and Journey's path escaping, on the <c>storage.json</c> filenames.
/// </summary>
public sealed class FilenameVectorTests
{
    [Fact]
    public void Every_filename_sanitizes_escapes_and_formats_like_rails( )
    {
        var filenames = Cases( "filenames" );
        Assert.Equal( 16, filenames.Count );

        foreach ( var vector in filenames )
        {
            var input = Convert.FromHexString( vector["input_hex"].RequiredString( ) );
            var filename = BlobFilename.FromBytes( input );
            var sanitized = filename.Sanitized;

            Assert.Equal( vector["sanitized"].RequiredString( ), sanitized );
            Assert.Equal( vector["inline"].RequiredString( ), ContentDisposition.Format( "inline", sanitized ) );
            Assert.Equal( vector["attachment"].RequiredString( ), ContentDisposition.Format( "attachment", sanitized ) );
            Assert.Equal( vector["escaped_path"].RequiredString( ), ContentDisposition.EscapePath( sanitized ) );
        }
    }

    [Fact]
    public void Base_and_extension_match_for_every_valid_utf8_name( )
    {
        var strict = new UTF8Encoding( encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true );
        var checkedNames = 0;
        foreach ( var vector in Cases( "filenames" ) )
        {
            var input = Convert.FromHexString( vector["input_hex"].RequiredString( ) );
            string raw;
            try
            {
                raw = strict.GetString( input );
            }
            catch ( DecoderFallbackException )
            {
                // Ruby computes base and extension on the raw bytes; a .NET string can't hold them.
                continue;
            }

            var filename = new BlobFilename( raw );
            Assert.Equal( vector["base_hex"].RequiredString( ), Convert.ToHexStringLower( Encoding.UTF8.GetBytes( filename.Base ) ) );
            Assert.Equal( vector["extension_hex"].RequiredString( ), Convert.ToHexStringLower( Encoding.UTF8.GetBytes( filename.Extension ) ) );
            checkedNames++;
        }

        Assert.Equal( 15, checkedNames );
    }

    [Theory]
    [InlineData( "foo.", ".", "foo" )]
    [InlineData( "a.b.", ".", "a.b" )]
    [InlineData( ".bashrc", "", ".bashrc" )]
    [InlineData( "..", "", ".." )]
    [InlineData( "...", "", "..." )]
    [InlineData( ".a.b", ".b", ".a" )]
    [InlineData( "a..b", ".b", "a." )]
    [InlineData( "a/b.c/d", "", "d" )]
    [InlineData( "a.b/", ".b", "a" )]
    [InlineData( "x.tar.gz", ".gz", "x.tar" )]
    [InlineData( "a/.b", "", ".b" )]
    [InlineData( ".", "", "." )]
    [InlineData( "", "", "" )]
    [InlineData( "é.png", ".png", "é" )]
    [InlineData( "a. b", ". b", "a" )]
    [InlineData( "foo. ", ". ", "foo" )]
    [InlineData( "/", "", "/" )]
    [InlineData( "a//", "", "a" )]
    [InlineData( "..a", "", "..a" )]
    [InlineData( "a...", ".", "a.." )]
    public void Extension_and_base_follow_rubys_file_methods( string name, string extension, string baseName )
    {
        // Checked against Ruby 3.4.10's File.extname and File.basename.
        var filename = new BlobFilename( name );

        Assert.Equal( extension, filename.ExtensionWithDelimiter );
        Assert.Equal( baseName, filename.Base );
    }

    [Fact]
    public void Sanitizing_strips_ascii_whitespace_and_nul_and_replaces_path_and_shell_characters( )
    {
        Assert.Equal( "a-b-c-d-e", new BlobFilename( "\0 \ta/b\\c:d<e\r\n\0" ).Sanitized );
        Assert.Equal( "-evil.exe", new BlobFilename( char.ConvertFromUtf32( 0x202E ) + "evil.exe" ).Sanitized );
        var unicodeSpaces = char.ConvertFromUtf32( 0x00A0 ) + "x" + char.ConvertFromUtf32( 0x3000 );
        Assert.Equal( unicodeSpaces, new BlobFilename( unicodeSpaces ).Sanitized );
    }

    [Fact]
    public void Transliteration_uses_the_default_approximations_and_question_marks( )
    {
        Assert.Equal( "AEther oeuvre.jpg", ContentDisposition.Transliterate( "Æther œuvre.jpg" ) );
        Assert.Equal( "???.png", ContentDisposition.Transliterate( "日本語.png" ) );
        Assert.Equal( "emoji ?.gif", ContentDisposition.Transliterate( "emoji 😀.gif" ) );
    }
}
