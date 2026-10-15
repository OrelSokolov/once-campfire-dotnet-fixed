using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Views;
using Campfire.Web.Views.Rendering;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Http;

public sealed class SplicedGzipTests
{
    [Fact]
    public async Task Empty_pages_and_oversized_incompressible_pieces_decode_without_being_retained( )
    {
        using var compressor = new SplicedGzip( );
        using ( var empty = Page( [], [] ) )
        {
            await AssertDecodesAsync( compressor.Create( empty, TestContext.Current.CancellationToken ), ReadOnlyMemory<byte>.Empty );
        }

        var random = new Random( 42 );
        var chars = new char[500000];
        for ( var index = 0; index < chars.Length; index++ )
        {
            chars[index] = ( char ) random.Next( 32, 127 );
        }

        var fragment = new CachedFragment( SafeHtml.Raw( new string( chars ) ) );
        using var page = Page( [fragment], [] );
        await AssertDecodesAsync( compressor.Create( page, TestContext.Current.CancellationToken ), page.Bytes );
        var encoded = compressor.Create( page, TestContext.Current.CancellationToken );
        Assert.True( encoded.Pieces[0].Length > GzipPieceStore.MaximumPieceBytes );
        Assert.Equal( 0, compressor.CacheSnapshot.FragmentCount );
        Assert.False( encoded.Pieces[0].Equals( compressor.Create( page, TestContext.Current.CancellationToken ).Pieces[0] ) );
        await AssertDecodesAsync( encoded, page.Bytes );
    }

    [Fact]
    public async Task Predecessor_and_glue_changes_never_reuse_the_wrong_piece_and_a_fifth_variant_ages_out( )
    {
        var target = new CachedFragment( SafeHtml.Raw( string.Concat( Enumerable.Repeat( "<article>shared 東京</article>", 200 ) ) ) );
        var predecessors = Enumerable.Range( 0, 6 )
            .Select( number => new CachedFragment( SafeHtml.Raw( string.Concat( Enumerable.Repeat( $"<article>before {number} 👋</article>", 200 ) ) ) ) )
            .ToArray( );
        using var compressor = new SplicedGzip( );
        ReadOnlyMemory<byte> first = default, last = default;
        for ( var index = 0; index < predecessors.Length; index++ )
        {
            using var page = Page( [predecessors[index], target], "\n"u8 );
            await AssertDecodesAsync( compressor.Create( page, TestContext.Current.CancellationToken ), page.Bytes );
            var encoded = compressor.Create( page, TestContext.Current.CancellationToken );
            await AssertDecodesAsync( encoded, page.Bytes );
            if ( index == 0 )
            {
                first = encoded.Pieces[1];
            }

            last = encoded.Pieces[1];
        }

        using ( var page = Page( [predecessors[^1], target], "\n"u8 ) )
        {
            var encoded = compressor.Create( page, TestContext.Current.CancellationToken );
            Assert.True( last.Equals( encoded.Pieces[1] ) );
            await AssertDecodesAsync( encoded, page.Bytes );
        }

        using ( var page = Page( [predecessors[0], target], "\n"u8 ) )
        {
            var encoded = compressor.Create( page, TestContext.Current.CancellationToken );
            Assert.False( first.Equals( encoded.Pieces[1] ) );
            await AssertDecodesAsync( encoded, page.Bytes );
        }

        using ( var page = Page( [predecessors[0], target], " \t"u8 ) )
        {
            await AssertDecodesAsync( compressor.Create( page, TestContext.Current.CancellationToken ), page.Bytes );
        }
    }

    [Fact]
    public async Task Eviction_keeps_active_responses_valid_and_charges_stay_bounded( )
    {
        using var compressor = new SplicedGzip( );
        // The store retains compressed bytes and identity chains, not fragment markup.
        // Apply pressure with retainable, poorly compressible payloads rather than large HTML.
        var random = new Random( 42 );
        var chars = new char[200000];
        for ( var index = 0; index < chars.Length; index++ )
        {
            chars[index] = ( char ) random.Next( 32, 127 );
        }

        var repeated = new string( chars );
        var firstFragment = new CachedFragment( SafeHtml.Raw( repeated + "first" ) );
        using var firstPage = Page( [firstFragment], [] );
        await AssertDecodesAsync( compressor.Create( firstPage, TestContext.Current.CancellationToken ), firstPage.Bytes );
        var first = compressor.Create( firstPage, TestContext.Current.CancellationToken );
        Assert.True( first.Pieces[0].Length <= GzipPieceStore.MaximumPieceBytes );
        for ( var number = 0; number < 240; number++ )
        {
            using var page = Page( [new CachedFragment( SafeHtml.Raw( repeated + number ) )], [] );
            compressor.Create( page, TestContext.Current.CancellationToken );
            compressor.Create( page, TestContext.Current.CancellationToken );
            Assert.True( compressor.CacheSnapshot.FragmentBytes <= GzipPieceStore.FragmentBudget );
        }

        await AssertDecodesAsync( first, firstPage.Bytes );
        var regenerated = compressor.Create( firstPage, TestContext.Current.CancellationToken );
        Assert.False( first.Pieces[0].Equals( regenerated.Pieces[0] ) );
        await AssertDecodesAsync( regenerated, firstPage.Bytes );
    }

    [Fact]
    public async Task Whole_members_are_reused_only_for_identical_bodies_and_remain_bounded( )
    {
        using var compressor = new SplicedGzip( );
        var plain = Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( "<p>whole member 👋</p>", 4000 ) ) );
        var first = compressor.CreateMember( plain, TestContext.Current.CancellationToken );
        Assert.Equal( 0, compressor.CacheSnapshot.MemberCount );
        var again = compressor.CreateMember( plain, TestContext.Current.CancellationToken );
        Assert.False( first.Bytes.Equals( again.Bytes ) );
        Assert.True( again.Bytes.Equals( compressor.CreateMember( plain, TestContext.Current.CancellationToken ).Bytes ) );
        for ( var number = 0; number < 160; number++ )
        {
            var changed = plain.Concat( Encoding.UTF8.GetBytes( number.ToString( CultureInfo.InvariantCulture ) ) ).ToArray( );
            var encoded = compressor.CreateMember( changed, TestContext.Current.CancellationToken );
            compressor.CreateMember( changed, TestContext.Current.CancellationToken );
            await AssertDecodesAsync( encoded, changed );
            Assert.True( compressor.CacheSnapshot.MemberBytes <= GzipPieceStore.MemberBudget );
        }

        await AssertDecodesAsync( first, plain );
        Assert.False( again.Bytes.Equals( compressor.CreateMember( plain, TestContext.Current.CancellationToken ).Bytes ) );
    }

    private static PooledBytesBody Page( CachedFragment[] fragments, ReadOnlySpan<byte> glue )
    {
        using var buffer = new Utf8PageBuffer( computeDigest: false );
        var recorder = new PageRecorder( buffer );
        foreach ( var fragment in fragments )
        {
            recorder.Mark( fragment );
            buffer.Write( fragment.Bytes );
            buffer.Write( glue );
        }

        return buffer.TakeBody( fragments: recorder.Take( buffer.Length ) );
    }

    private static async Task AssertDecodesAsync( ResponseBody encoded, ReadOnlyMemory<byte> expected )
    {
        var httpContext = new DefaultHttpContext( );
        using var wire = new MemoryStream( );
        httpContext.Response.Body = wire;
        await encoded.WriteAsync( httpContext.Response, TestContext.Current.CancellationToken );
        Assert.Equal( wire.Length, encoded.Length );
        wire.Position = 0;
        using var gzip = new GZipStream( wire, CompressionMode.Decompress );
        using var plain = new MemoryStream( );
        await gzip.CopyToAsync( plain, TestContext.Current.CancellationToken );
        Assert.Equal( expected.ToArray( ), plain.ToArray( ) );
    }

    [Fact]
    public void An_unchanged_cached_fragment_reuses_its_compressed_payload( )
    {
        var fragment = new CachedFragment( SafeHtml.Raw( string.Concat( Enumerable.Repeat( "<article>cached 👋</article>", 100 ) ) ) );
        using var buffer = new Utf8PageBuffer( computeDigest: false );
        var recorder = new PageRecorder( buffer );
        recorder.Mark( fragment );
        buffer.Write( fragment.Bytes );
        using var body = buffer.TakeBody( fragments: recorder.Take( buffer.Length ) );
        using var compressor = new SplicedGzip( );
        compressor.Create( body, TestContext.Current.CancellationToken );
        var first = compressor.Create( body, TestContext.Current.CancellationToken );
        var second = compressor.Create( body, TestContext.Current.CancellationToken );
        Assert.True( MemoryMarshal.TryGetArray( first.Pieces[0], out var firstSegment ) );
        Assert.True( MemoryMarshal.TryGetArray( second.Pieces[0], out var secondSegment ) );
        Assert.Same( firstSegment.Array, secondSegment.Array );
    }

    [Fact]
    public void Combined_crc_matches_the_standard_vector_and_a_whole_unicode_buffer( )
    {
        Assert.Equal( 0xcbf43926u, Crc32Combine.Combine( Crc32.HashToUInt32( "1234"u8 ), Crc32.HashToUInt32( "56789"u8 ), 5 ) );
        var bytes = Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( "👋 Ελληνικά <strong>東京</strong>", 4000 ) ) );
        foreach ( var split in new[] { 0, 1, 32768, 70000, bytes.Length } )
        {
            Assert.Equal(
                Crc32.HashToUInt32( bytes ),
                Crc32Combine.Combine( Crc32.HashToUInt32( bytes.AsSpan( 0, split ) ), Crc32.HashToUInt32( bytes.AsSpan( split ) ), bytes.Length - split ) );
        }
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Splicing_preserves_unicode_large_predecessors_glue_and_fragment_order( bool reverse )
    {
        var first = new CachedFragment( SafeHtml.Raw( string.Concat( Enumerable.Repeat( "<article>first 👋 Ελληνικά</article>", 2500 ) ) ) );
        var second = new CachedFragment( SafeHtml.Raw( string.Concat( Enumerable.Repeat( "<article>second 東京</article>", 2500 ) ) ) );
        var fragments = reverse ? new[] { second, first, second } : [first, second, first];
        using var buffer = new Utf8PageBuffer( computeDigest: false );
        var recorder = new PageRecorder( buffer );
        buffer.Write( "<main>prefix 🧪</main>"u8 );
        foreach ( var fragment in fragments )
        {
            recorder.Mark( fragment );
            buffer.Write( fragment.Bytes );
            buffer.Write( "\n"u8 );
        }

        buffer.Write( "<footer>tail</footer>"u8 );
        using var body = buffer.TakeBody( fragments: recorder.Take( buffer.Length ) );
        Assert.Equal( fragments, body.Fragments.Select( recorded => recorded.Fragment ) );
        foreach ( var recorded in body.Fragments )
        {
            Assert.Equal( recorded.Fragment.Bytes.ToArray( ), body.Bytes.Slice( recorded.Offset, recorded.Fragment.Bytes.Length ).ToArray( ) );
        }

        var split = PageParts.Split( body );
        Assert.Equal( body.Bytes.Length, split.Sum( part => part.Length ) );
        Assert.Contains( split, part => part.Glue == 1 );
        using var compressor = new SplicedGzip( );
        var encoded = compressor.Create( body, TestContext.Current.CancellationToken );
        var httpContext = new DefaultHttpContext( );
        using var wire = new MemoryStream( );
        httpContext.Response.Body = wire;
        await encoded.WriteAsync( httpContext.Response, TestContext.Current.CancellationToken );
        Assert.Equal( wire.Length, encoded.Length );
        wire.Position = 0;
        using var decoded = new MemoryStream( );
        await using ( var gzip = new GZipStream( wire, CompressionMode.Decompress, leaveOpen: true ) )
        {
            await gzip.CopyToAsync( decoded, TestContext.Current.CancellationToken );
        }

        Assert.Equal( body.Bytes.ToArray( ), decoded.ToArray( ) );
    }
}
