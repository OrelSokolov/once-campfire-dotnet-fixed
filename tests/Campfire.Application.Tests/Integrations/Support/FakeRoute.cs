using System.Text;

namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>A canned response, matched by method, <c>Host</c> (without its port; "*" for any) and target.</summary>
internal sealed record FakeRoute( string Method, string Host, string Path, int Status )
{
    public string Reason { get; init; } = "Status";

    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    public byte[] Body { get; init; } = [];

    /// <summary>Sent with <c>Transfer-Encoding: chunked</c> in 64 KiB chunks.</summary>
    public bool Chunked { get; init; }

    /// <summary>Gzipped, with <c>Content-Encoding: gzip</c>.</summary>
    public bool Gzip { get; init; }

    /// <summary>How long to wait before answering.</summary>
    public TimeSpan Delay { get; init; }

    public FakeRoute Header( string name, string value ) => this with { Headers = [.. Headers, new( name, value )] };

    public FakeRoute WithBody( string body ) => this with { Body = Encoding.UTF8.GetBytes( body ) };
}
