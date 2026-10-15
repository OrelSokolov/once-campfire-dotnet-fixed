using System.Text;

namespace Campfire.IntegrationTests.Support;

/// <summary>A request a <see cref="LoopbackServer"/> received.</summary>
public sealed record LoopbackRequest( string Method, string Host, string PathAndQuery, IReadOnlyDictionary<string, string> Headers, byte[] Body )
{
    /// <summary>The body as UTF-8 text.</summary>
    public string BodyText => Encoding.UTF8.GetString( Body );
}
