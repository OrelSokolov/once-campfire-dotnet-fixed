using System.Text.Json.Nodes;
using Campfire.Application.Storage.Marcel;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary><c>Marcel::MimeType.for(io, name:, declared_type:)</c> on the <c>storage.json</c> samples.</summary>
public sealed class MarcelVectorTests
{
    [Fact]
    public void Every_sample_is_identified_as_marcel_identifies_it( )
    {
        var samples = Cases( "marcel" );
        Assert.Equal( 51, samples.Count );

        foreach ( var sample in samples )
        {
            var data = Data( sample );
            var name = sample["name"].OptionalString( );
            var declared = sample["declared_type"].OptionalString( );

            Assert.True(
                sample["content_type"].RequiredString( ) == MarcelMimeType.Identify( data, name, declared ),
                $"{name} declared {declared ?? "nil"}: expected {sample["content_type"].RequiredString( )}, got " +
                    $"{MarcelMimeType.Identify( data, name, declared )}" );
        }
    }

    [Fact]
    public void The_leading_magic_window_identifies_like_the_whole_file( )
    {
        Assert.True( MarcelMimeType.MagicWindow >= 8192, $"The window covers the HTML range match: {MarcelMimeType.MagicWindow}" );

        foreach ( var sample in Cases( "marcel" ) )
        {
            var data = Data( sample );
            var head = data.AsSpan( 0, Math.Min( data.Length, MarcelMimeType.MagicWindow ) );

            Assert.Equal(
                MarcelMimeType.Identify( data, sample["name"].OptionalString( ), sample["declared_type"].OptionalString( ) ),
                MarcelMimeType.Identify( head, sample["name"].OptionalString( ), sample["declared_type"].OptionalString( ) ) );
        }
    }

    [Fact]
    public void Extensions_resolve_case_insensitively_with_or_without_a_dot( )
    {
        Assert.Equal( "image/jpeg", MarcelMimeType.ByExtension( "JPG" ) );
        Assert.Equal( "image/jpeg", MarcelMimeType.ByExtension( ".jpeg" ) );
        Assert.Null( MarcelMimeType.ByExtension( "png " ) );
        Assert.Equal( "application/octet-stream", MarcelMimeType.ForExtension( "definitely-not-an-extension" ) );
        Assert.Equal( ["jpg", "jpeg", "jpe", "jif", "jfif", "jfi"], MarcelMimeType.Extensions( "image/jpeg" ) );
        Assert.True( MarcelMimeType.IsChild( "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip" ) );
    }

    private static byte[] Data( JsonObject sample ) =>
        sample["fixture"].OptionalString( ) is { } fixture
            ? File.ReadAllBytes( Fixture( fixture ) )
            : Convert.FromHexString( sample["data_hex"].RequiredString( ) );
}
