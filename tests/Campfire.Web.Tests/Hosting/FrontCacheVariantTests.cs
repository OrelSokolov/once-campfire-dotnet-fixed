using System.Globalization;
using Campfire.Web.Hosting;
using Campfire.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Campfire.Web.Tests.Hosting;

public sealed class FrontCacheVariantTests
{
    private static readonly ServiceProvider CompressionServices = new ServiceCollection( ).AddLogging( ).AddCampfireCompression( ).BuildServiceProvider( );
    [Fact]
    public void A_url_retains_at_most_sixty_four_distinct_vary_values_and_evicts_its_least_recent_variant( )
    {
        var cache = new FrontResponseCache( new FrontServerOptions( ) );
        for ( var index = 0; index < 64; index++ )
        {
            var variant = Language( index );
            cache.Store( variant.Request, variant.Response, [( byte ) index], TimeSpan.FromHours( 1 ) );
        }

        // Touch variant zero; adding the 65th distinct language must evict variant one.
        Assert.NotNull( cache.Find( Language( 0 ).Request ) );
        var newest = Language( 64 );
        cache.Store( newest.Request, newest.Response, [64], TimeSpan.FromHours( 1 ) );
        Assert.NotNull( cache.Find( Language( 0 ).Request ) );
        Assert.Null( cache.Find( Language( 1 ).Request ) );
        for ( var index = 2; index <= 64; index++ )
        {
            Assert.Equal( new byte[] { ( byte ) index }, cache.Find( Language( index ).Request )!.Body );
        }
    }

    [Theory]
    [InlineData( "br", "br", true )]
    [InlineData( "br", "gzip", false )]
    [InlineData( "*;q=0", "identity", false )]
    [InlineData( "*;q=0", "br", false )]
    [InlineData( "*;q=0", "gzip", false )]
    [InlineData( "", "identity", true )]
    [InlineData( "", "br", false )]
    [InlineData( "", "gzip", false )]
    public void Cache_lookup_respects_brotli_wildcard_rejection_and_a_missing_encoding_header( string accepted, string stored, bool matches )
    {
        var cache = new FrontResponseCache( new FrontServerOptions( ) );
        var source = Context( "/edge", "gzip,br" );
        source.Response.Headers.ContentEncoding = stored == "identity" ? null : stored;
        cache.Store( source.Request, source.Response, "representation"u8.ToArray( ), TimeSpan.FromHours( 1 ) );
        var hit = cache.Find( Context( "/edge", accepted ).Request );
        Assert.Equal( matches, hit is not null );
        if ( matches )
        {
            Assert.Equal( "representation"u8.ToArray( ), hit!.Body );
            Assert.Equal( stored == "identity" ? "" : stored, hit.Headers.GetValueOrDefault( "Content-Encoding" ).ToString( ) );
        }
    }

    private static DefaultHttpContext Language( int index )
    {
        var httpContext = Context( "/languages", "gzip" );
        httpContext.Request.Headers.AcceptLanguage = "language-" + index.ToString( CultureInfo.InvariantCulture );
        httpContext.Response.Headers.Vary = "Accept-Encoding,Accept-Language";
        return httpContext;
    }

    [Fact]
    public void Adversarial_encoding_variants_are_bounded_per_url_and_do_not_evict_another_hot_url( )
    {
        var cache = new FrontResponseCache( new FrontServerOptions { CacheBytes = 3500, MaximumCacheItemBytes = 1024 } );
        var cold = Context( "/cold", "gzip" );
        cache.Store( cold.Request, cold.Response, new byte[800], TimeSpan.FromHours( 1 ) );
        var hot = Context( "/hot", "gzip" );
        cache.Store( hot.Request, hot.Response, new byte[800], TimeSpan.FromHours( 1 ) );
        Assert.NotNull( cache.Find( hot.Request ) );
        for ( var index = 0; index < 3000; index++ )
        {
            var attacker = Context( "/variants", "identity,unknown" + index.ToString( CultureInfo.InvariantCulture ) );
            cache.Store( attacker.Request, attacker.Response, new byte[800], TimeSpan.FromHours( 1 ) );
        }

        Assert.Null( cache.Find( cold.Request ) ); // Budget pressure really evicted the least-recent URL.
        Assert.NotNull( cache.Find( hot.Request ) ); // Never touched during the attack.
        Assert.NotNull( cache.Find( Context( "/variants", "identity,unknown0" ).Request ) );
        Assert.NotNull( cache.Find( Context( "/variants", "identity,unknown2999" ).Request ) );
        Assert.NotNull( cache.Find( Context( "/variants", "gzip" ).Request ) );
        Assert.Null( cache.Find( Context( "/variants", "gzip,identity;q=0" ).Request ) );
    }

    [Fact]
    public void Stored_dates_are_not_replayed_and_changed_vary_schemas_cannot_mix_responses( )
    {
        var cache = new FrontResponseCache( new FrontServerOptions( ) );
        var first = Context( "/", "gzip" );
        first.Response.Headers.Date = "Thu, 01 Jan 1970 00:00:00 GMT";
        cache.Store( first.Request, first.Response, "first"u8.ToArray( ), TimeSpan.FromHours( 1 ) );
        Assert.False( cache.Find( first.Request )!.Headers.ContainsKey( "Date" ) );
        first.Response.Headers.Vary = "Accept-Language";
        first.Request.Headers.AcceptLanguage = "en";
        cache.Store( first.Request, first.Response, "second"u8.ToArray( ), TimeSpan.FromHours( 1 ) );
        Assert.Null( cache.Find( Context( "/", "gzip" ).Request ) );
        Assert.Equal( "second"u8.ToArray( ), cache.Find( first.Request )!.Body );
    }

    private static DefaultHttpContext Context( string path, string encoding )
    {
        var httpContext = new DefaultHttpContext { RequestServices = CompressionServices };
        httpContext.Request.Method = "GET";
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString( "example.test" );
        httpContext.Request.Path = path;
        if ( encoding.Length != 0 )
        {
            httpContext.Request.Headers.AcceptEncoding = encoding;
        }

        httpContext.Response.Headers.Vary = "Accept-Encoding";
        if ( Compression.NegotiatedEncoding( httpContext.Request ) == "gzip" )
        {
            httpContext.Response.Headers.ContentEncoding = "gzip";
        }

        return httpContext;
    }

    [Fact]
    public void Browsers_advertising_brotli_reuse_an_available_gzip_representation( )
    {
        var cache = new FrontResponseCache( new FrontServerOptions( ) );
        var browser = Context( "/gzip-only", "gzip,deflate,br" );
        browser.Response.Headers.CacheControl = "public,max-age=3600";
        cache.Store( browser.Request, browser.Response, "gzip bytes"u8.ToArray( ), TimeSpan.FromHours( 1 ) );
        browser.Request.Headers.IfNoneMatch = "\"conditional\"";
        var hit = cache.Find( browser.Request );
        Assert.NotNull( hit );
        Assert.Equal( "gzip", hit.Headers["Content-Encoding"].ToString( ) );
        Assert.Equal( "public,max-age=3600", hit.Headers["Cache-Control"].ToString( ) );
        Assert.Equal( "gzip bytes"u8.ToArray( ), hit.Body );
        Assert.Null( cache.Find( Context( "/gzip-only", "br,gzip;q=0,identity;q=0" ).Request ) );
    }

    [Fact]
    public void A_precompressed_body_is_keyed_by_its_actual_encoding_not_the_deflaters_choice( )
    {
        var cache = new FrontResponseCache( new FrontServerOptions( ) );
        var source = Context( "/encoded", "br;q=0,gzip;q=1" );
        source.Response.Headers.ContentEncoding = "br";
        cache.Store( source.Request, source.Response, "brotli bytes"u8.ToArray( ), TimeSpan.FromHours( 1 ) );
        Assert.Null( cache.Find( Context( "/encoded", "gzip" ).Request ) );
        Assert.Null( cache.Find( source.Request ) );
        var brotli = cache.Find( Context( "/encoded", "br" ).Request );
        Assert.NotNull( brotli );
        Assert.Equal( "br", brotli.Headers["Content-Encoding"].ToString( ) );
        Assert.Equal( "brotli bytes"u8.ToArray( ), brotli.Body );

        var gzip = Context( "/encoded", "gzip" );
        cache.Store( gzip.Request, gzip.Response, "gzip bytes"u8.ToArray( ), TimeSpan.FromHours( 1 ) );
        var hit = cache.Find( gzip.Request );
        Assert.NotNull( hit );
        Assert.Equal( "gzip", hit.Headers["Content-Encoding"].ToString( ) );
        Assert.Equal( "gzip bytes"u8.ToArray( ), hit.Body );
        Assert.Equal( "brotli bytes"u8.ToArray( ), cache.Find( Context( "/encoded", "br" ).Request )!.Body );
    }
}
