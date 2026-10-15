namespace Campfire.Web.Views.Caching;

/// <summary>Shutdown summaries expose cumulative cache activity without request logging or tracing.</summary>
internal static partial class PageCacheLog
{
    [LoggerMessage( Level = LogLevel.Information, Message = "Response cache charge current={Current} peak={Peak} budget={Budget}" )]
    internal static partial void Charge( ILogger logger, long current, long peak, long budget );

    [LoggerMessage( Level = LogLevel.Information, Message = "Response cache counter route={Route} event={Event} reason=[{Reason}] count={Count}" )]
    internal static partial void Counter( ILogger logger, string route, string @event, string reason, long count );
}
