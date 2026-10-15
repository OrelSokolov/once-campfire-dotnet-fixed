namespace Campfire.Application.Storage.Http;

/// <summary>The lower-case header names Rack 3 responses use, as the file, blob and static asset servers send them.</summary>
public static class RackHeaderNames
{
    public const string AcceptEncoding = "accept-encoding";
    public const string AcceptRanges = "accept-ranges";
    public const string Allow = "allow";
    public const string CacheControl = "cache-control";
    public const string ContentDisposition = "content-disposition";
    public const string ContentEncoding = "content-encoding";
    public const string ContentLength = "content-length";
    public const string ContentRange = "content-range";
    public const string ContentTransferEncoding = "content-transfer-encoding";
    public const string ContentType = "content-type";
    public const string LastModified = "last-modified";
    public const string Vary = "vary";

    /// <summary>Rack's "not handled here, try the next app" marker.</summary>
    public const string Cascade = "x-cascade";
}
