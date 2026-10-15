namespace Campfire.Web.Workers;

/// <summary>
/// <c>Room::PushMessageJob.perform_later(room, message)</c>: pick the subscriptions, then queue a delivery to each on
/// the Web Push pool (a thread pool in Rails, not an Active Job).
/// </summary>
public sealed record PushMessageJob( long RoomId, long MessageId );
