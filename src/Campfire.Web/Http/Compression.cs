using System.Collections.Frozen;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Campfire.Web.Http;

/// <summary><c>Rack::Deflater</c>'s gzip: the response compression middleware plus the spliced gzip of rendered pages.</summary>
public static class Compression
{
    /// <summary>The media types gzip compresses (parameters such as <c>charset</c> aside).</summary>
    private static readonly FrozenSet<string> TextMediaTypes = FrozenSet.Create(
        StringComparer.Ordinal,
        ContentTypes.Html,
        ContentTypes.PlainText,
        ContentTypes.TurboStream,
        ContentTypes.Css,
        ContentTypes.JavaScript,
        ContentTypes.ApplicationJavaScript,
        ContentTypes.Json,
        ContentTypes.Manifest,
        ContentTypes.Svg );

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> TextMediaTypeLookup =
        TextMediaTypes.GetAlternateLookup<ReadOnlySpan<char>>( );

    public static IServiceCollection AddCampfireCompression( this IServiceCollection services )
    {
        services.AddSingleton<SplicedGzip>( );
        services.AddResponseCompression( options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<GzipCompressionProvider>( );
            options.MimeTypes = TextMediaTypes;
        } );
        services.Configure<GzipCompressionProviderOptions>( options => options.Level = CompressionLevel.Optimal );
        services.Replace( ServiceDescriptor.Singleton<IResponseCompressionProvider, TunnelAwareCompressionProvider>( ) );
        return services;
    }

    public static string NegotiatedEncoding( HttpRequest request )
    {
        var provider = request.HttpContext.RequestServices.GetRequiredService<IResponseCompressionProvider>( );
        return NegotiatedEncoding( request, provider );
    }

    public static bool IsWebSocketAccepted( HttpContext httpContext ) => httpContext.Features.Get<AcceptedTunnel>( )?.Accepting == true;

    /// <summary>Whether a <c>Content-Type</c> (its parameters ignored) is one gzip compresses.</summary>
    internal static bool IsTextMedia( string? contentType )
    {
        var mime = contentType.AsSpan( );
        var separator = mime.IndexOf( ';' );
        if ( separator >= 0 )
        {
            mime = mime[..separator];
        }

        return TextMediaTypeLookup.Contains( mime );
    }

    internal static string NegotiatedEncoding( HttpRequest request, IResponseCompressionProvider provider )
    {
        if ( !provider.CheckRequestAcceptsCompression( request.HttpContext ) )
        {
            return ContentCodings.Identity;
        }

        return provider.GetCompressionProvider( request.HttpContext )?.EncodingName ?? ContentCodings.Identity;
    }
}
