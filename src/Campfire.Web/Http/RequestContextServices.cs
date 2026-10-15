namespace Campfire.Web.Http;

/// <summary>The singletons every <see cref="RequestContext"/> shares: options, the business clock and the HTTP logger.</summary>
public sealed class RequestContextServices( HttpOptions options, TimeProvider clock, ILoggerFactory loggerFactory )
{
    /// <summary>The log category of request failures and the request layer.</summary>
    public const string LoggerCategory = "Campfire.Http";

    public HttpOptions Options { get; } = options;
    public TimeProvider Clock { get; } = clock;
    public ILogger Logger { get; } = loggerFactory.CreateLogger( LoggerCategory );
}
