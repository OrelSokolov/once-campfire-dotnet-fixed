namespace Campfire.Web.Tests.Cable.Support;

/// <summary>An HTTP/1.1 response read off a raw socket: status, headers (lowercased names) and body.</summary>
public sealed record RawHttpResponse( int Status, IReadOnlyDictionary<string, string> Headers, string Body );
