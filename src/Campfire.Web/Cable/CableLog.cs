namespace Campfire.Web.Cable;

/// <summary>The cable server's log messages, worded after Action Cable's.</summary>
internal static partial class CableLog
{
    [LoggerMessage( Level = LogLevel.Debug, Message = "Successfully upgraded to WebSocket" )]
    public static partial void Upgraded( ILogger logger );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Cable accepted {Protocol}; permessage-deflate={Compressed}" )]
    public static partial void NegotiatedCompression( ILogger logger, string protocol, bool compressed );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Registered connection ({Identifier})" )]
    public static partial void Registered( ILogger logger, string identifier );

    [LoggerMessage( Level = LogLevel.Debug, Message = "WebSocket closed abruptly" )]
    public static partial void ClosedAbruptly( ILogger logger, Exception error );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Aborting a WebSocket whose write failed or timed out" )]
    public static partial void WriteFailed( ILogger logger, Exception error );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Disconnecting a client that fell behind ({Identifier})" )]
    public static partial void SlowClient( ILogger logger, string? identifier );

    [LoggerMessage( Level = LogLevel.Error, Message = "Could not authenticate the connection" )]
    public static partial void AuthenticationFailed( ILogger logger, Exception error );

    [LoggerMessage( Level = LogLevel.Error, Message = "An unauthorized connection attempt was rejected" )]
    public static partial void Unauthorized( ILogger logger );

    [LoggerMessage( Level = LogLevel.Error, Message = "Couldn't handle non-string message: Array" )]
    public static partial void NonStringMessage( ILogger logger );

    [LoggerMessage( Level = LogLevel.Error, Message = "Closing a connection that sent a message over {Limit} bytes" )]
    public static partial void MessageTooBig( ILogger logger, int limit );

    [LoggerMessage( Level = LogLevel.Information, Message = "Removing connection ({Identifier})" )]
    public static partial void RemovingConnection( ILogger logger, string? identifier );

    [LoggerMessage( Level = LogLevel.Error, Message = "Could not execute command from ({Message})" )]
    public static partial void CouldNotExecute( ILogger logger, string message, Exception? error = null );

    [LoggerMessage( Level = LogLevel.Error, Message = "Received unrecognized command in {Message}" )]
    public static partial void UnrecognizedCommand( ILogger logger, string message );

    [LoggerMessage( Level = LogLevel.Error, Message = "Could not unsubscribe {Identifier}" )]
    public static partial void UnsubscribeFailed( ILogger logger, string identifier, Exception error );

    [LoggerMessage( Level = LogLevel.Error, Message = "Could not execute command: {Problem}" )]
    public static partial void InvalidCommand( ILogger logger, string problem );

    [LoggerMessage( Level = LogLevel.Error, Message = "Subscription class not found: {Channel}" )]
    public static partial void ChannelNotFound( ILogger logger, string channel );

    [LoggerMessage( Level = LogLevel.Error, Message = "Unable to find subscription with identifier: {Identifier}" )]
    public static partial void SubscriptionNotFound( ILogger logger, string? identifier );

    [LoggerMessage( Level = LogLevel.Error, Message = "Unable to process {Channel}#{Action}" )]
    public static partial void UnableToProcess( ILogger logger, string channel, string action );

    [LoggerMessage( Level = LogLevel.Debug, Message = "[ActionCable] Broadcasting to {Broadcasting}" )]
    public static partial void Broadcasting( ILogger logger, string broadcasting );

    [LoggerMessage( Level = LogLevel.Information, Message = "Closing {Count} cable connections for shutdown" )]
    public static partial void ClosingForShutdown( ILogger logger, int count );

    [LoggerMessage( Level = LogLevel.Error, Message = "Request origin not allowed: {Origin}" )]
    public static partial void OriginNotAllowed( ILogger logger, string? origin );
}
