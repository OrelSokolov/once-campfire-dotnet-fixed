namespace Campfire.Application.Pushes;

/// <summary>What <c>Room::MessagePusher#build_payload</c> sends.</summary>
public sealed record PushPayload( string Title, string Body, string Path );
