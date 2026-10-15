using Microsoft.AspNetCore.HttpOverrides;

namespace Campfire.Web.Http;

/// <summary>Forwarding extensions not supplied by the framework's HeaderNames.</summary>
public static class ForwardedHeaderNames
{
    public static string For { get; } = ForwardedHeadersDefaults.XForwardedForHeaderName;
    public static string Host { get; } = ForwardedHeadersDefaults.XForwardedHostHeaderName;
    public static string Proto { get; } = ForwardedHeadersDefaults.XForwardedProtoHeaderName;
    public const string Port = "X-Forwarded-Port";
    public const string Ssl = "X-Forwarded-Ssl";
    public const string Scheme = "X-Forwarded-Scheme";
    public const string ClientIp = "Client-Ip";
    public const string Forwarded = "Forwarded";
}
