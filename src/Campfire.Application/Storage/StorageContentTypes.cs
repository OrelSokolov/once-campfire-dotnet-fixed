using System.Net.Mime;

namespace Campfire.Application.Storage;

/// <summary>
/// The effective <c>config.active_storage</c> content type lists: the Rails 8.2 engine defaults with
/// <c>load_defaults 8.2</c>, minus the types <c>reference/config/initializers/vips.rb</c> removes.
/// </summary>
public static class StorageContentTypes
{
    /// <summary><c>ActiveStorage.binary_content_type</c>.</summary>
    public const string Binary = MediaTypeNames.Application.Octet;

    /// <summary>
    /// <c>variable_content_types</c>: the engine default less <c>image/bmp</c>,
    /// <c>image/vnd.microsoft.icon</c> and <c>image/vnd.adobe.photoshop</c> (config/initializers/vips.rb).
    /// </summary>
    public static IReadOnlyList<string> Variable { get; } =
        ["image/png", "image/gif", "image/jpeg", "image/tiff", "image/webp", "image/avif", "image/heic", "image/heif"];

    /// <summary><c>web_image_content_types</c> (<c>image/webp</c> added by <c>load_defaults 7.2</c>).</summary>
    public static IReadOnlyList<string> WebImage { get; } = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    /// <summary><c>content_types_allowed_inline</c>.</summary>
    public static IReadOnlyList<string> AllowedInline { get; } =
    [
        "image/webp", "image/avif", "image/png", "image/gif", "image/jpeg", "image/tiff", "image/bmp", "image/vnd.adobe.photoshop",
        "image/vnd.microsoft.icon", "application/pdf",
    ];

    /// <summary><c>content_types_to_serve_as_binary</c>.</summary>
    public static IReadOnlyList<string> ServeAsBinary { get; } =
    [
        "text/html", "image/svg+xml", "application/postscript", "application/x-shockwave-flash", "text/xml", "application/xml",
        "application/xhtml+xml", "application/mathml+xml", "text/cache-manifest",
    ];

    /// <summary>
    /// <c>ActiveStorage.video_preview_arguments</c> from <c>load_defaults 7.0</c>, already
    /// <c>Shellwords.split</c>: the first keyframe or scene change, else the first frame.
    /// </summary>
    public static IReadOnlyList<string> VideoPreviewArguments { get; } =
    [
        "-vf", @"select=eq(n\,0)+eq(key\,1)+gt(scene\,0.015),loop=loop=-1:size=2,trim=start_frame=1", "-frames:v", "1", "-f", "image2",
    ];

    public static bool IsVariable( string? contentType ) => contentType is not null && Variable.Contains( contentType );

    public static bool IsWebImage( string? contentType ) => contentType is not null && WebImage.Contains( contentType );

    public static bool IsAllowedInline( string? contentType ) => contentType is not null && AllowedInline.Contains( contentType );

    public static bool IsServedAsBinary( string? contentType ) => contentType is not null && ServeAsBinary.Contains( contentType );

    /// <summary><c>Blob#content_type_for_serving</c>.</summary>
    public static string? ForServing( string? contentType ) => IsServedAsBinary( contentType ) ? Binary : contentType;

    /// <summary><c>Blob#forced_disposition_for_serving</c>: <c>"attachment"</c> for binary or non-inline types, else <c>null</c>.</summary>
    public static string? ForcedDisposition( string? contentType ) =>
        IsServedAsBinary( contentType ) || !IsAllowedInline( contentType ) ? Http.ContentDisposition.Attachment : null;
}
