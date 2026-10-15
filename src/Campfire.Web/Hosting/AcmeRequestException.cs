using System.Net;
using System.Net.Http.Headers;

namespace Campfire.Web.Hosting;

/// <summary>An ACME server's error response: its status, problem type and when to retry.</summary>
internal sealed class AcmeRequestException( HttpStatusCode status, string? problem, RetryConditionHeaderValue? retry, DateTimeOffset now )
    : HttpRequestException( $"ACME request failed ({( int ) status}): {problem}", null, status )
{
    internal bool IsRateLimited => status == HttpStatusCode.TooManyRequests || problem == AcmeProtocol.RateLimited;
    internal DateTimeOffset? RetryAt { get; } = retry?.Date ?? (retry?.Delta is { } delta ? now + delta : null);
}
