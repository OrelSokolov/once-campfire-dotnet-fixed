using System.Buffers;
using System.IO.Compression;
using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Views;
using Campfire.Web.Views.Rendering;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Http;

public sealed class SplicedGzipHistoryTests
{
    [Fact]
    public async Task Colliding_text_identities_cannot_reuse_a_fragment_with_a_different_dictionary( )
    {
        var fragment = Fragment( "distinctive dictionary alpha 東京", 16000 );
        using var first = Page( [fragment], Encoding.UTF8.GetBytes( new string( 'a', 40000 ) + fragment.Html ), [] );
        using var second = Page( [fragment], Encoding.UTF8.GetBytes( new string( 'b', 40000 ) + new string( 'z', fragment.Bytes.Length ) ), [] );
        using var compressor = new SplicedGzip( );
        var encoded = await WarmAsync( compressor, first );
        var store = new GzipPieceStore( );
        var identity = GzipKeyHash.TextIdentity( first.Bytes.Span[..100] );
        var firstParts = PageParts.Split( first );
        var secondParts = PageParts.Split( second );
        var firstRun = new GzipRunHistory( );
        firstRun.Advance( firstParts[0], identity, first.Bytes );
        store.Store(
            fragment,
            fragment.Digest,
            firstRun.Before( firstParts[1] ),
            firstParts[1].Bytes( first.Bytes.Span ),
            [],
            new GzipPiece( encoded.Pieces[1].ToArray( ), 0 ) );
        Assert.Equal( 32768, firstRun.Before( firstParts[1] ).Copy( ).TextDictionary.Length );
        Assert.Equal( 32 + 32768, store.TextSuffixBytes );
        Assert.True( store.Snapshot( ).FragmentBytes >= GzipPieceStore.IndexReserveBytes + 32768 );
        var secondRun = new GzipRunHistory( );
        secondRun.Advance( secondParts[0], identity, second.Bytes );
        Assert.NotNull( store.Find( fragment, fragment.Digest, firstRun.Before( firstParts[1] ), firstParts[1].Bytes( first.Bytes.Span ), [] ) );
        Assert.Null( store.Find( fragment, fragment.Digest, secondRun.Before( secondParts[1] ), secondParts[1].Bytes( second.Bytes.Span ), [] ) );
        await DecodeAsync( await WarmAsync( compressor, second ), second.Bytes );
    }

    [Fact]
    public async Task Different_heads_do_not_prevent_reuse_from_the_second_fragment( )
    {
        var fragments = Enumerable.Range( 0, 6 ).Select( number => Fragment( $"<article>shared {number} 東京</article>", 4096 ) ).ToArray( );
        using var first = Page( fragments, "first head"u8, "\n"u8 );
        using var second = Page( fragments.Select( fragment => new CachedFragment( SafeHtml.Raw( fragment.Html ) ) ).ToArray( ), "another head"u8, "\n"u8 );
        using var compressor = new SplicedGzip( );
        var firstEncoded = await WarmAsync( compressor, first );
        var secondEncoded = compressor.Create( second, TestContext.Current.CancellationToken );
        Assert.False( firstEncoded.Pieces[1].Equals( secondEncoded.Pieces[1] ) );
        for ( var index = 2; index <= fragments.Length; index++ )
        {
            Assert.True( firstEncoded.Pieces[index].Equals( secondEncoded.Pieces[index] ) );
        }

        await DecodeAsync( firstEncoded, first.Bytes );
        await DecodeAsync( secondEncoded, second.Bytes );
    }

    [Fact]
    public async Task An_identical_predecessor_after_different_earlier_fragments_does_not_reuse( )
    {
        var older = Fragment( "earlier alpha shared with target 👋", 4096 );
        var other = Fragment( "earlier beta unrelated to target 🧪", 4096 );
        var predecessor = Fragment( "unchanged neutral predecessor", 4096 );
        var target = Fragment( "earlier alpha shared with target 👋", 4096 );
        using var first = Page( [older, predecessor, target], [], "\n"u8 );
        using var second = Page( [other, predecessor, target], [], "\n"u8 );
        using var compressor = new SplicedGzip( );
        var firstEncoded = await WarmAsync( compressor, first );
        var secondEncoded = await WarmAsync( compressor, second );
        Assert.False( firstEncoded.Pieces[2].Equals( secondEncoded.Pieces[2] ) );
        await DecodeAsync( firstEncoded, first.Bytes );
        await DecodeAsync( secondEncoded, second.Bytes );
    }

    [Fact]
    public async Task Window_cuts_drop_older_identity_but_keep_the_run_start_flag( )
    {
        var suffix = Enumerable.Range( 0, 8 ).Select( number => Fragment( $"common suffix {number}", 4096 ) ).ToArray( );
        var target = Fragment( "common suffix 2", 4096 );
        using var shortRun = Page( [.. suffix, target], [], [] );
        using var longRun = Page( [Fragment( "older alpha", 4096 ), .. suffix, target], [], [] );
        using var otherLongRun = Page( [Fragment( "older beta", 4096 ), .. suffix, target], [], [] );
        using var compressor = new SplicedGzip( );
        var shortEncoded = await WarmAsync( compressor, shortRun );
        var longEncoded = await WarmAsync( compressor, longRun );
        var otherLongEncoded = await WarmAsync( compressor, otherLongRun );
        Assert.False( shortEncoded.Pieces[^2].Equals( longEncoded.Pieces[^2] ) );
        Assert.True( longEncoded.Pieces[^2].Equals( otherLongEncoded.Pieces[^2] ) );
        await DecodeAsync( shortEncoded, shortRun.Bytes );
        await DecodeAsync( longEncoded, longRun.Bytes );
        await DecodeAsync( otherLongEncoded, otherLongRun.Bytes );
    }

    [Fact]
    public async Task Every_piece_decodes_with_only_its_keyed_history_and_replaced_older_bytes( )
    {
        // Only fragment two repeats the head's distinctive bytes. Fragment one must not
        // supply that text: otherwise an encoder leaking head history can escape detection.
        var head = Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( "UNKEYED HEAD distinctive sentinel 東京 ", 160 ) ) );
        var fragments = Enumerable.Range( 0, 12 )
            .Select( index => Fragment( index % 2 == 1 ? "UNKEYED HEAD distinctive sentinel 東京" : "different alternating source 👋", 6000 ) )
            .ToArray( );
        using var compressor = new SplicedGzip( );
        for ( var attempt = 0; attempt < 2; attempt++ )
        {
            if ( attempt == 1 )
            {
                fragments[6] = Fragment( "changed piece after cache hits", 6000 );
            }

            using var page = Page( fragments, head, " \n"u8 );
            // Stored pieces obey the keyed-history proof; one-off pieces are response-local
            // and intentionally allowed to use actual preceding bytes instead.
            var encoded = await WarmAsync( compressor, page );
            await DecodeAsync( encoded, page.Bytes );
            var parts = PageParts.Split( page );
            var runStart = 0;
            for ( var index = 0; index < parts.Count; index++ )
            {
                var part = parts[index];
                var dictionary = ReadOnlyMemory<byte>.Empty;
                if ( index > 0 )
                {
                    if ( part.Fragment is not null && parts[index - 1].Fragment is not null )
                    {
                        var start = Math.Max( runStart, part.Offset - 32768 );
                        dictionary = page.Bytes.Slice( start, part.Offset - start );
                    }
                    else
                    {
                        var previous = parts[index - 1];
                        var start = previous.Offset + previous.Glue;
                        var length = previous.Length - previous.Glue;
                        dictionary = page.Bytes.Slice( start + Math.Max( 0, length - 32768 ), Math.Min( length, 32768 ) );
                    }
                }

                AssertPiece( encoded.Pieces[index], dictionary, page.Bytes.Slice( part.Offset, part.Length ) );
                if ( part.Fragment is null )
                {
                    runStart = part.Offset + part.Length;
                }
            }
        }
    }

    [Fact]
    public async Task Concurrent_cold_and_warm_runs_share_valid_bounded_pieces( )
    {
        using var page = Page( Enumerable.Range( 0, 10 ).Select( number => Fragment( $"concurrent {number} 東京", 7000 ) ).ToArray( ), "head"u8, "\n"u8 );
        using var compressor = new SplicedGzip( );
        var creating = Enumerable.Range( 0, 16 )
            .Select( _ => Task.Run( ( ) => compressor.Create( page, TestContext.Current.CancellationToken ), TestContext.Current.CancellationToken ) );
        var responses = await Task.WhenAll( creating );
        foreach ( var response in responses )
        {
            await DecodeAsync( response, page.Bytes );
        }

        Assert.True( compressor.CacheSnapshot.FragmentBytes <= GzipPieceStore.FragmentBudget );
        Assert.True( compressor.CacheSnapshot.TextBytes <= GzipPieceStore.TextBudget );
    }

    private static CachedFragment Fragment( string pattern, int length )
    {
        var text = string.Concat( Enumerable.Repeat( pattern, length ) );
        return new CachedFragment( SafeHtml.Raw( text[..length] ) );
    }

    private static async Task<SplicedGzipBody> WarmAsync( SplicedGzip compressor, PooledBytesBody page )
    {
        for ( var index = 0; index < 2; index++ )
        {
            await DecodeAsync( compressor.Create( page, TestContext.Current.CancellationToken ), page.Bytes );
        }

        return compressor.Create( page, TestContext.Current.CancellationToken );
    }

    private static PooledBytesBody Page( CachedFragment[] fragments, ReadOnlySpan<byte> head, ReadOnlySpan<byte> glue )
    {
        using var buffer = new Utf8PageBuffer( computeDigest: false );
        var recorder = new PageRecorder( buffer );
        buffer.Write( head );
        foreach ( var fragment in fragments )
        {
            recorder.Mark( fragment );
            buffer.Write( fragment.Bytes );
            buffer.Write( glue );
        }

        return buffer.TakeBody( fragments: recorder.Take( buffer.Length ) );
    }

    private static void AssertPiece( ReadOnlyMemory<byte> piece, ReadOnlyMemory<byte> history, ReadOnlyMemory<byte> expected )
    {
        var prefix = Enumerable.Repeat( ( byte ) '!', 32768 ).Concat( history.ToArray( ) ).ToArray( );
        using var encoder = new DeflateEncoder( 6 );
        using var wire = new MemoryStream( );
        Span<byte> scratch = stackalloc byte[16384];
        ReadOnlySpan<byte> source = prefix;
        while ( !source.IsEmpty )
        {
            var status = encoder.Compress( source, scratch, out var consumed, out var written, false );
            Assert.NotEqual( OperationStatus.InvalidData, status );
            Assert.True( consumed != 0 || written != 0 );
            wire.Write( scratch[..written] );
            source = source[consumed..];
        }

        while ( true )
        {
            var status = encoder.Flush( scratch, out var written );
            wire.Write( scratch[..written] );
            if ( status == OperationStatus.Done )
            {
                break;
            }

            Assert.Equal( OperationStatus.DestinationTooSmall, status );
        }

        wire.Write( piece.Span );
        wire.Write( [3, 0] );
        wire.Position = 0;
        using var inflater = new DeflateStream( wire, CompressionMode.Decompress );
        using var plain = new MemoryStream( );
        inflater.CopyTo( plain );
        Assert.Equal( prefix.Length + expected.Length, plain.Length );
        Assert.Equal( expected.ToArray( ), plain.ToArray( )[prefix.Length..] );
    }

    private static async Task DecodeAsync( ResponseBody body, ReadOnlyMemory<byte> expected )
    {
        var httpContext = new DefaultHttpContext( );
        using var wire = new MemoryStream( );
        httpContext.Response.Body = wire;
        await body.WriteAsync( httpContext.Response, TestContext.Current.CancellationToken );
        wire.Position = 0;
        using var gzip = new GZipStream( wire, CompressionMode.Decompress );
        using var plain = new MemoryStream( );
        await gzip.CopyToAsync( plain, TestContext.Current.CancellationToken );
        Assert.Equal( expected.ToArray( ), plain.ToArray( ) );
    }
}
