namespace Campfire.Web.Http;

/// <summary>
/// The registered types: <c>action_dispatch/http/mime_types.rb</c>, then turbo-rails'
/// <c>:turbo_stream</c>, in registration order (the order matters for <c>text/*</c> expansion).
/// </summary>
public static class Mime
{
    public static readonly MimeType Html = new( "html", ContentTypes.Html, ["application/xhtml+xml"], ["xhtml"] );
    public static readonly MimeType Text = new( "text", ContentTypes.PlainText, [], ["txt"] );
    public static readonly MimeType Js = new( "js", ContentTypes.JavaScript, [ContentTypes.ApplicationJavaScript, "application/x-javascript"], [] );
    public static readonly MimeType Css = new( "css", ContentTypes.Css, [], [] );
    public static readonly MimeType Ics = new( "ics", "text/calendar", [], [] );
    public static readonly MimeType Csv = new( "csv", "text/csv", [], [] );
    public static readonly MimeType Vcf = new( "vcf", "text/vcard", [], [] );
    public static readonly MimeType Vtt = new( "vtt", "text/vtt", [], ["vtt"] );
    public static readonly MimeType Md = new( "md", "text/markdown", [], ["md", "markdown"] );
    public static readonly MimeType Png = new( "png", "image/png", [], ["png"] );
    public static readonly MimeType Jpeg = new( "jpeg", "image/jpeg", [], ["jpg", "jpeg", "jpe", "pjpeg"] );
    public static readonly MimeType Gif = new( "gif", "image/gif", [], ["gif"] );
    public static readonly MimeType Bmp = new( "bmp", "image/bmp", [], ["bmp"] );
    public static readonly MimeType Tiff = new( "tiff", "image/tiff", [], ["tif", "tiff"] );
    public static readonly MimeType Svg = new( "svg", ContentTypes.Svg, [], [] );
    public static readonly MimeType Webp = new( "webp", "image/webp", [], ["webp"] );
    public static readonly MimeType Mpeg = new( "mpeg", "video/mpeg", [], ["mpg", "mpeg", "mpe"] );
    public static readonly MimeType Mp3 = new( "mp3", "audio/mpeg", [], ["mp1", "mp2", "mp3"] );
    public static readonly MimeType Ogg = new( "ogg", "audio/ogg", [], ["oga", "ogg", "spx", "opus"] );
    public static readonly MimeType M4a = new( "m4a", "audio/aac", ["audio/mp4"], ["m4a", "mpg4", "aac"] );
    public static readonly MimeType Webm = new( "webm", "video/webm", [], ["webm"] );
    public static readonly MimeType Mp4 = new( "mp4", "video/mp4", [], ["mp4", "m4v"] );
    public static readonly MimeType Otf = new( "otf", "font/otf", [], ["otf"] );
    public static readonly MimeType Ttf = new( "ttf", "font/ttf", [], ["ttf"] );
    public static readonly MimeType Woff = new( "woff", "font/woff", [], ["woff"] );
    public static readonly MimeType Woff2 = new( "woff2", "font/woff2", [], ["woff2"] );
    public static readonly MimeType Xml = new( "xml", "application/xml", ["text/xml", "application/x-xml"], [] );
    public static readonly MimeType Rss = new( "rss", "application/rss+xml", [], [] );
    public static readonly MimeType Atom = new( "atom", "application/atom+xml", [], [] );
    public static readonly MimeType Yaml = new( "yaml", "application/x-yaml", ["text/yaml"], ["yml", "yaml"] );
    public static readonly MimeType MultipartForm = new( "multipart_form", ContentTypes.MultipartForm, [], [] );
    public static readonly MimeType UrlEncodedForm = new( "url_encoded_form", ContentTypes.UrlEncodedForm, [], [] );
    public static readonly MimeType Json = new( "json", ContentTypes.Json, ["text/x-json", "application/jsonrequest", "application/problem+json"], [] );
    public static readonly MimeType Pdf = new( "pdf", "application/pdf", [], ["pdf"] );
    public static readonly MimeType Zip = new( "zip", "application/zip", [], ["zip"] );
    public static readonly MimeType Gzip = new( "gzip", "application/gzip", ["application/x-gzip"], ["gz"] );
    public static readonly MimeType TurboStream = new( "turbo_stream", ContentTypes.TurboStream, [], [] );

    /// <summary><c>Mime::ALL</c>: the <c>*/*</c> wildcard, only meaningful in negotiation.</summary>
    public static readonly MimeType All = new( "*/*", "*/*", [], [] );

    /// <summary>Every registered type, in registration order.</summary>
    public static IReadOnlyList<MimeType> Registered { get; } =
    [
        Html, Text, Js, Css, Ics, Csv, Vcf, Vtt, Md, Png, Jpeg, Gif, Bmp, Tiff, Svg, Webp, Mpeg, Mp3, Ogg, M4a, Webm, Mp4,
        Otf, Ttf, Woff, Woff2, Xml, Rss, Atom, Yaml, MultipartForm, UrlEncodedForm, Json, Pdf, Zip, Gzip, TurboStream,
    ];

    /// <summary><c>Mime[ext]</c> / <c>Mime::Type.lookup_by_extension</c>.</summary>
    public static MimeType? LookupByExtension( string extension )
    {
        ArgumentNullException.ThrowIfNull( extension );

        foreach ( var mime in Registered )
        {
            if ( mime.Symbol == extension || mime.Extensions.Contains( extension ) )
            {
                return mime;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>Mime::Type.lookup</c>: the exact type string or a synonym, else the part before <c>;</c>.
    /// <c>null</c> is a valid but unregistered type; an invalid one throws
    /// <see cref="InvalidMimeTypeException"/> (<c>Mime::Type::InvalidMimeType</c>).
    /// </summary>
    public static MimeType? Lookup( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        if ( LookupExact( value ) is { } exact )
        {
            return exact;
        }

        var semicolon = value.IndexOf( ';', StringComparison.Ordinal );
        var baseType = (semicolon < 0 ? value : value[..semicolon]).TrimEnd( );
        if ( LookupExact( baseType ) is { } registered )
        {
            return registered;
        }

        if ( baseType == "*/*" )
        {
            return All;
        }

        return IsValidMimeType( baseType ) ? null : throw new InvalidMimeTypeException( value );
    }

    private static MimeType? LookupExact( string value )
    {
        foreach ( var mime in Registered )
        {
            if ( mime.Value == value || mime.Synonyms.Contains( value ) )
            {
                return mime;
            }
        }

        return null;
    }

    /// <summary><c>Mime::Type::MIME_REGEXP</c>, loosely: <c>type/subtype</c> made of token characters.</summary>
    private static bool IsValidMimeType( string baseType )
    {
        var slash = baseType.IndexOf( '/', StringComparison.Ordinal );
        if ( slash < 0 )
        {
            return false;
        }

        var subtype = baseType[(slash + 1)..];

        return IsToken( baseType[..slash] ) && (subtype == "*" || IsToken( subtype ));
    }

    private static bool IsToken( string name )
    {
        if ( name.Length == 0 || name.Length > 127 || !char.IsAsciiLetterOrDigit( name[0] ) )
        {
            return false;
        }

        foreach ( var character in name )
        {
            if ( !char.IsAsciiLetterOrDigit( character ) && !"!#$&-^_.+".Contains( character, StringComparison.Ordinal ) )
            {
                return false;
            }
        }

        return true;
    }
}
