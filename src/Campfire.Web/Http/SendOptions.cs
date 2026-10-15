using ContentDisposition = Campfire.Application.Storage.Http.ContentDisposition;

namespace Campfire.Web.Http;

/// <summary>Options for <c>send_file</c> / <c>send_data</c>.</summary>
public sealed record SendOptions
{
    public string? Filename { get; init; }

    /// <summary><c>type:</c>; defaults to the filename's registered type, else <c>application/octet-stream</c>.</summary>
    public string? ContentType { get; init; }

    /// <summary><c>disposition:</c>; <c>null</c> omits the header. Defaults to <c>attachment</c>.</summary>
    public string? Disposition { get; init; } = ContentDisposition.Attachment;

    public int Status { get; init; } = StatusCodes.Status200OK;

    /// <summary>Honor <c>Range</c> requests (206/416). Rails' <c>send_file</c> doesn't; Active Storage's proxy does.</summary>
    public bool Ranges { get; init; }

    public static SendOptions Inline( string contentType ) => new( ) { ContentType = contentType, Disposition = ContentDisposition.Inline };
}
