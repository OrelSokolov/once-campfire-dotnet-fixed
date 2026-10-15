using Campfire.Application.Data;

namespace Campfire.Application.Pushes;

/// <summary>
/// <c>params.require(:push_subscription).permit(:endpoint, :p256dh_key, :auth_key)</c>: each key either given (its
/// value cast to the string column, or null) or absent. <c>find_by</c> and <c>create</c> only see the keys given.
/// </summary>
/// <param name="Endpoint">The push service URL.</param>
/// <param name="P256dhKey">The browser's public key.</param>
/// <param name="AuthKey">The browser's auth secret.</param>
public sealed record PushSubscriptionParams( Assigned<string?> Endpoint, Assigned<string?> P256dhKey, Assigned<string?> AuthKey );
