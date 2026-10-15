namespace Campfire.Web.Http;

/// <summary>HTTP representation encodings; file suffixes remain separate from their wire names.</summary>
public static class ContentCodings
{
    public const string Identity = "identity";
    public const string Gzip = "gzip";
    public const string Brotli = "br";
}
