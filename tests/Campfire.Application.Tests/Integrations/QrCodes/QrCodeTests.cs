using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Integrations.QrCodes;
using Campfire.Application.Ruby;

namespace Campfire.Application.Tests.Integrations.QrCodes;

/// <summary>
/// The rqrcode port against <c>vectors/qr/rqrcode.json</c> (generated in the reference image by
/// <c>reference-tools/campfire/rqrcode.rb</c>): the version, every module, and the full SVG where the vector has it.
/// </summary>
public sealed class QrCodeTests
{
    public static TheoryData<int> Vectors => [.. Enumerable.Range( 0, Load( ).Count )];

    private static JsonArray Load( ) => RepositoryPaths.ReadVectorJson( "qr", "rqrcode.json" ).AsArray( );

    [Fact]
    public void The_vector_file_covers_every_mode_and_many_versions( )
    {
        var vectors = Load( );

        Assert.Equal( 39, vectors.Count );
        Assert.Equal( 5, vectors.Count( vector => vector!["svg"] is not null ) );
        Assert.True( vectors.Select( vector => vector!["version"]!.GetValue<int>( ) ).Distinct( ).Count( ) >= 15 );
    }

    [Theory]
    [MemberData( nameof( Vectors ) )]
    public void Matches_rqrcode( int index )
    {
        var vector = Load( )[index]!;
        var input = Convert.FromBase64String( vector["input_base64"]!.GetValue<string>( ) );

        var modules = QrCode.Modules( input, out var version );

        Assert.Equal( vector["version"]!.GetValue<int>( ), version );
        Assert.Equal( vector["modules"]!.GetValue<string>( ), Rows( modules ) );
        if ( vector["svg"] is { } svg )
        {
            Assert.Equal( svg.GetValue<string>( ), QrCode.Svg( input ) );
        }
    }

    [Fact]
    public void Decodes_the_id_like_ruby_urlsafe_decode64( )
    {
        Assert.Equal( "http://campfire.test"u8.ToArray( ), QrCode.DecodeId( "aHR0cDovL2NhbXBmaXJlLnRlc3Q" ) );
        Assert.Equal( "http://campfire.test"u8.ToArray( ), QrCode.DecodeId( "aHR0cDovL2NhbXBmaXJlLnRlc3Q=" ) );
        Assert.Equal( [0xfb, 0xff], QrCode.DecodeId( "-_8" ) );
        Assert.Equal( [0xfb, 0xff], QrCode.DecodeId( "+/8" ) );
        Assert.Empty( QrCode.DecodeId( "" )! );
        foreach ( var invalid in new[] { "a", "ab=c", "aB==", "a*bc", "aGk=aGk=", "aGk" + "é" } )
        {
            Assert.Null( QrCode.DecodeId( invalid ) );
        }
    }

    [Fact]
    public void Encodes_what_it_decodes( )
    {
        var bytes = Encoding.UTF8.GetBytes( "http://campfire.test/session/transfers/x?y=1" );

        Assert.Equal( bytes, RubyBase64.UrlSafeDecode( RubyBase64.UrlSafeEncode( bytes ) ) );
        Assert.Equal( bytes, RubyBase64.UrlSafeDecode( RubyBase64.UrlSafeEncode( bytes, padding: false ) ) );
    }

    /// <summary>Oversized input raises as rqrcode does (a 500 in Rails), at exactly version 40's level H capacity.</summary>
    [Theory]
    [InlineData( '9', 3056 )]
    [InlineData( 'A', 1852 )]
    [InlineData( 'a', 1273 )]
    public void Refuses_data_past_version_40( char filler, int fits )
    {
        var largest = Encoding.ASCII.GetBytes( new string( filler, fits ) );
        var tooLarge = Encoding.ASCII.GetBytes( new string( filler, fits + 1 ) );

        _ = QrCode.Modules( largest, out var version );
        Assert.Equal( 40, version );
        var error = Assert.Throws<QrCodeCapacityException>( ( ) => QrCode.Svg( tooLarge ) );
        Assert.Equal( "Data length exceed maximum capacity of version 40", error.Message );
        Assert.Throws<QrCodeCapacityException>( ( ) => QrCode.Svg( Encoding.ASCII.GetBytes( new string( filler, 1_000_000 ) ) ) );
    }

    private static string Rows( bool[,] modules )
    {
        var count = modules.GetLength( 0 );
        var rows = new List<string>( count );
        for ( var row = 0; row < count; row++ )
        {
            var line = new StringBuilder( count );
            for ( var column = 0; column < count; column++ )
            {
                line.Append( modules[row, column] ? '1' : '0' );
            }

            rows.Add( line.ToString( ) );
        }

        return string.Join( '\n', rows );
    }
}
