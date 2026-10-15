using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Campfire.Application.Ruby;


namespace Campfire.Application.Integrations.Webhooks;

/// <summary>
/// <c>Mime::Type.lookup(string)</c> in the reference app (Action Dispatch's registrations plus turbo-rails'): a
/// registered type by its string or a synonym, else a new unregistered type, which must match
/// <c>Mime::Type::MIME_REGEXP</c> or raise <c>Mime::Type::InvalidMimeType</c>.
/// </summary>
internal static partial class MimeLookup
{
    /// <summary><c>Mime::LOOKUP</c>: each registered string and synonym, with the type's symbol and <c>to_s</c>.</summary>
    private static readonly FrozenDictionary<string, (string Symbol, string Type)> Registered =
        new Dictionary<string, (string, string)>( StringComparer.Ordinal )
        {
            ["text/html"] = ("html", "text/html"),
            ["application/xhtml+xml"] = ("html", "text/html"),
            ["text/plain"] = ("text", "text/plain"),
            ["text/javascript"] = ("js", "text/javascript"),
            ["application/javascript"] = ("js", "text/javascript"),
            ["application/x-javascript"] = ("js", "text/javascript"),
            ["text/css"] = ("css", "text/css"),
            ["text/calendar"] = ("ics", "text/calendar"),
            ["text/csv"] = ("csv", "text/csv"),
            ["text/vcard"] = ("vcf", "text/vcard"),
            ["text/vtt"] = ("vtt", "text/vtt"),
            ["vtt"] = ("vtt", "text/vtt"),
            ["text/markdown"] = ("md", "text/markdown"),
            ["image/png"] = ("png", "image/png"),
            ["image/jpeg"] = ("jpeg", "image/jpeg"),
            ["image/gif"] = ("gif", "image/gif"),
            ["image/bmp"] = ("bmp", "image/bmp"),
            ["image/tiff"] = ("tiff", "image/tiff"),
            ["image/svg+xml"] = ("svg", "image/svg+xml"),
            ["image/webp"] = ("webp", "image/webp"),
            ["video/mpeg"] = ("mpeg", "video/mpeg"),
            ["audio/mpeg"] = ("mp3", "audio/mpeg"),
            ["audio/ogg"] = ("ogg", "audio/ogg"),
            ["audio/aac"] = ("m4a", "audio/aac"),
            ["audio/mp4"] = ("m4a", "audio/aac"),
            ["video/webm"] = ("webm", "video/webm"),
            ["video/mp4"] = ("mp4", "video/mp4"),
            ["font/otf"] = ("otf", "font/otf"),
            ["font/ttf"] = ("ttf", "font/ttf"),
            ["font/woff"] = ("woff", "font/woff"),
            ["font/woff2"] = ("woff2", "font/woff2"),
            ["application/xml"] = ("xml", "application/xml"),
            ["text/xml"] = ("xml", "application/xml"),
            ["application/x-xml"] = ("xml", "application/xml"),
            ["application/rss+xml"] = ("rss", "application/rss+xml"),
            ["application/atom+xml"] = ("atom", "application/atom+xml"),
            ["application/x-yaml"] = ("yaml", "application/x-yaml"),
            ["text/yaml"] = ("yaml", "application/x-yaml"),
            ["multipart/form-data"] = ("multipart_form", "multipart/form-data"),
            ["application/x-www-form-urlencoded"] = ("url_encoded_form", "application/x-www-form-urlencoded"),
            ["application/json"] = ("json", "application/json"),
            ["text/x-json"] = ("json", "application/json"),
            ["application/jsonrequest"] = ("json", "application/json"),
            ["application/problem+json"] = ("json", "application/json"),
            ["application/pdf"] = ("pdf", "application/pdf"),
            ["application/zip"] = ("zip", "application/zip"),
            ["application/gzip"] = ("gzip", "application/gzip"),
            ["application/x-gzip"] = ("gzip", "application/gzip"),
            ["text/vnd.turbo-stream.html"] = ("turbo_stream", "text/vnd.turbo-stream.html"),
        }.ToFrozenDictionary( StringComparer.Ordinal );

    /// <summary>
    /// The type's symbol (null for an unregistered type) and its <c>to_s</c>, which for a synonym is the registered
    /// type rather than what was sent. Throws <see cref="WebhookDeliveryException"/> (<c>Mime::Type::InvalidMimeType</c>).
    /// </summary>
    public static (string? Symbol, string Type) Lookup( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        if ( Registered.TryGetValue( value, out var registered ) )
        {
            return registered;
        }

        // fall back to the media type without parameters: string.split(";", 2)[0]&.rstrip
        var semicolon = value.IndexOf( ';' );
        var media = RubyStrings.Rstrip( semicolon < 0 ? value : value[..semicolon] );
        if ( Registered.TryGetValue( media, out registered ) )
        {
            return registered;
        }

        if ( media.Length == 0 || !MimeRegexp( ).IsMatch( media ) )
        {
            throw new WebhookDeliveryException( "Mime::Type::InvalidMimeType", $"\"{media}\" is not a valid MIME type" );
        }

        return (null, media);
    }

    /// <summary>A type, subtype, parameter name or value in <c>MIME_REGEXP</c>.</summary>
    private const string MimeToken = """[a-zA-Z0-9][a-zA-Z0-9!#$&\-^_.+]{0,126}""";

    /// <summary>
    /// <c>Mime::Type::MIME_REGEXP</c> (in the Ruby source, "\s" inside the double-quoted parameter pattern is a literal
    /// space). Non-backtracking, so a crafted header can't make it slow.
    /// </summary>
    [GeneratedRegex(
        """\A(?:\*/\*|""" + MimeToken + """/(?:\*|""" + MimeToken + """)(?: *; *""" + MimeToken
            + """(?:=(?:""" + MimeToken + """|"[^"\r\\]*"))?)*[ \t\n\x0B\x0C\r]*)\z""",
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant )]
    private static partial Regex MimeRegexp( );
}
