using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Views;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Tests.Http;

/// <summary>Changing any recorded content changes the validator; different splits can only cost a cache miss.</summary>
public sealed class PageValidatorTests
{
    [Theory]
    [InlineData( "text" )]
    [InlineData( "fragment" )]
    [InlineData( "glue" )]
    public void Every_content_kind_changes_the_validator( string changed )
    {
        using var first = Page( "head", "first", "\n" );
        using var second = Page(
            changed == "text" ? "other head" : "head",
            changed == "fragment" ? "other fragment" : "first",
            changed == "glue" ? " " : "\n" );

        Assert.NotEqual( first.Bytes.ToArray( ), second.Bytes.ToArray( ) );
        Assert.NotEqual( first.Digest, second.Digest );
    }

    [Fact]
    public void New_and_reused_fragment_instances_have_the_same_validator( )
    {
        using var first = Page( "head", "first", "\n" );
        var cached = new CachedFragment( SafeHtml.Raw( string.Concat( Enumerable.Repeat( "first", 600 ) ) ) );
        using var second = Page( "head", "first", "\n", cached );
        using var warm = Page( "head", "first", "\n", cached );

        Assert.Equal( first.Bytes.ToArray( ), second.Bytes.ToArray( ) );
        Assert.Equal( first.Digest, second.Digest );
        Assert.Equal( second.Digest, warm.Digest );
    }

    private static PooledBytesBody Page( string head, string fragmentText, string glue, CachedFragment? cached = null )
    {
        using var buffer = new Utf8PageBuffer( );
        var recorder = new PageRecorder( buffer );
        Write( buffer, Encoding.UTF8.GetBytes( head ) );
        foreach ( var text in new[] { fragmentText, "second" } )
        {
            var fragment = text == fragmentText && cached is not null
                ? cached
                : new CachedFragment( SafeHtml.Raw( string.Concat( Enumerable.Repeat( text, 600 ) ) ) );
            if ( text == "second" )
            {
                Write( buffer, Encoding.UTF8.GetBytes( glue ) );
            }

            recorder.Mark( fragment );
            Write( buffer, fragment.Bytes );
        }

        Write( buffer, "tail"u8 );
        return buffer.TakeBody( recorder.Take( buffer.Length ) );
    }

    private static void Write( Utf8PageBuffer buffer, ReadOnlySpan<byte> bytes )
    {
        bytes.CopyTo( buffer.GetSpan( bytes.Length ) );
        buffer.Advance( bytes.Length );
    }
}
