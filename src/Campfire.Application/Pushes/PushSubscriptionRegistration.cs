using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Pushes;

/// <summary>
/// <c>Users::PushSubscriptionsController#create</c> (<c>app/controllers/users/push_subscriptions_controller.rb</c>): an
/// existing subscription matching the params is touched when it still validates; otherwise one is created when the
/// new one validates. Validation resolves the endpoint's host (<c>Push::Subscription#validate_endpoint_url</c>), which
/// is network I/O, so the web layer resolves every endpoint the write could validate
/// (<see cref="EndpointsToValidate"/>) before taking the writer lease and passes the outcomes in.
/// </summary>
public static class PushSubscriptionRegistration
{
    /// <summary>
    /// <c>@push_subscriptions.find_by(push_subscription_params)</c>: one condition per given key (a null value matches
    /// NULL), none when no key was given; <c>find_by</c> is <c>where(...).take</c>, with no order.
    /// </summary>
    public static PushSubscription? FindBy( CampfireDb db, long userId, PushSubscriptionParams attributes )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( attributes );

        var sql = $"SELECT {Columns.PushSubscriptions} FROM push_subscriptions WHERE push_subscriptions.user_id = @userId"
            + (attributes.Endpoint.IsSet ? " AND push_subscriptions.endpoint IS @endpoint" : "")
            + (attributes.P256dhKey.IsSet ? " AND push_subscriptions.p256dh_key IS @p256dhKey" : "")
            + (attributes.AuthKey.IsSet ? " AND push_subscriptions.auth_key IS @authKey" : "")
            + " LIMIT 1";

        return db.Connection.QueryFirstOrDefault<PushSubscription>(
            sql,
            new
            {
                userId,
                endpoint = attributes.Endpoint.Value,
                p256dhKey = attributes.P256dhKey.Value,
                authKey = attributes.AuthKey.Value,
            },
            db.Transaction );
    }

    /// <summary>
    /// The endpoints <see cref="Register"/> may validate: the matching subscription's, if there is one, and the one a
    /// new subscription would get (null when the key wasn't given).
    /// </summary>
    public static IReadOnlyList<string?> EndpointsToValidate( CampfireDb db, long userId, PushSubscriptionParams attributes )
    {
        ArgumentNullException.ThrowIfNull( attributes );

        var existing = FindBy( db, userId, attributes );
        var created = attributes.Endpoint.Value;
        return existing is null || existing.Endpoint == created ? [created] : [existing.Endpoint, created];
    }

    /// <summary>
    /// <c>create</c>'s write: find the matching subscription again (the transaction sees the current rows), then
    /// <c>subscription.valid?</c> and <c>touch</c>, or <c>create</c> with the user agent. <paramref name="isValid"/> answers
    /// the model's validations for an endpoint; an endpoint that wasn't resolved before the lease (a row that appeared
    /// or changed since) must answer false, so nothing unvalidated is ever kept.
    /// </summary>
    public static SubscribeOutcome Register(
        WriteScope scope, long userId, PushSubscriptionParams attributes, string? userAgent, Func<string?, bool> isValid )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( attributes );
        ArgumentNullException.ThrowIfNull( isValid );

        var db = scope.Db;
        var now = scope.Now;
        if ( FindBy( db, userId, attributes ) is { } existing )
        {
            if ( !isValid( existing.Endpoint ) )
            {
                return SubscribeOutcome.Invalid;
            }

            // subscription.touch: updated_at alone.
            existing.UpdatedAt = now;
            DataWrites.ExpectOneRow(
                db.Connection.Execute( "UPDATE push_subscriptions SET updated_at = @UpdatedAt WHERE id = @Id", existing, db.Transaction ),
                RecordKeys.PushSubscription( existing.Id ) );
            return SubscribeOutcome.Subscribed;
        }

        if ( !isValid( attributes.Endpoint.Value ) )
        {
            return SubscribeOutcome.Invalid;
        }

        var subscription = new PushSubscription
        {
            UserId = userId,
            Endpoint = attributes.Endpoint.Value,
            P256dhKey = attributes.P256dhKey.Value,
            AuthKey = attributes.AuthKey.Value,
            UserAgent = userAgent,
            CreatedAt = now,
            UpdatedAt = now,
        };
        subscription.Id = DataWrites.Save( ( ) => db.Connection.ExecuteScalar<long>(
            "INSERT INTO push_subscriptions (auth_key, created_at, endpoint, p256dh_key, updated_at, user_agent, user_id) " +
            "VALUES (@AuthKey, @CreatedAt, @Endpoint, @P256dhKey, @UpdatedAt, @UserAgent, @UserId) RETURNING id",
            subscription,
            db.Transaction ) );
        return SubscribeOutcome.Subscribed;
    }

    /// <summary>
    /// <c>Current.user.push_subscriptions.find_by(id:)</c>: null when it isn't the user's. <c>TestNotificationsController</c>
    /// uses <c>find</c>, which raises; its caller does.
    /// </summary>
    public static PushSubscription? FindByIdForUser( CampfireDb db, long userId, long subscriptionId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<PushSubscription>(
            $"SELECT {Columns.PushSubscriptions} FROM push_subscriptions " +
            "WHERE push_subscriptions.user_id = @userId AND push_subscriptions.id = @subscriptionId LIMIT 1",
            new
            {
                userId,
                subscriptionId
            },
            db.Transaction );
    }
}
