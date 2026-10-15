namespace Campfire.Application.Data;

/// <summary>
/// The column list each row type is read with, one per table, qualified with the table name so the same list works
/// in joins. Dapper.AOT binds result columns to members by name, ignoring case and underscores (<c>created_at</c> to
/// <c>CreatedAt</c>), so these lists and the row types' members are the whole mapping.
/// </summary>
/// <remarks>
/// The lists are constants so that the queries built from them are compile-time constants too, which lets Dapper.AOT
/// analyze each statement's parameters at build time. <see cref="ByTable"/> lets a test hold them to the schema.
/// </remarks>
internal static class Columns
{
    public const string Accounts =
        "accounts.id, accounts.created_at, accounts.custom_styles, accounts.join_code, accounts.name, accounts.settings, " +
        "accounts.singleton_guard, accounts.updated_at";

    public const string Users =
        "users.id, users.bio, users.bot_token, users.created_at, users.email_address, users.name, users.password_digest, " +
        "users.role, users.status, users.updated_at";

    public const string Bans = "bans.id, bans.created_at, bans.ip_address, bans.updated_at, bans.user_id";

    public const string Sessions =
        "sessions.id, sessions.created_at, sessions.ip_address, sessions.last_active_at, sessions.token, sessions.updated_at, " +
        "sessions.user_agent, sessions.user_id";

    public const string Searches = "searches.id, searches.created_at, searches.query, searches.updated_at, searches.user_id";

    public const string PushSubscriptions =
        "push_subscriptions.id, push_subscriptions.auth_key, push_subscriptions.created_at, push_subscriptions.endpoint, " +
        "push_subscriptions.p256dh_key, push_subscriptions.updated_at, push_subscriptions.user_agent, push_subscriptions.user_id";

    public const string Webhooks = "webhooks.id, webhooks.created_at, webhooks.updated_at, webhooks.url, webhooks.user_id";

    public const string Rooms = "rooms.id, rooms.created_at, rooms.creator_id, rooms.name, rooms.type, rooms.updated_at";

    public const string Memberships =
        "memberships.id, memberships.connected_at, memberships.connections, memberships.created_at, memberships.involvement, " +
        "memberships.room_id, memberships.unread_at, memberships.updated_at, memberships.user_id";

    public const string Messages =
        "messages.id, messages.client_message_id, messages.created_at, messages.creator_id, messages.room_id, messages.updated_at";

    public const string Boosts = "boosts.id, boosts.booster_id, boosts.content, boosts.created_at, boosts.message_id, boosts.updated_at";

    public const string RichTexts =
        "action_text_rich_texts.id, action_text_rich_texts.body, action_text_rich_texts.created_at, action_text_rich_texts.name, " +
        "action_text_rich_texts.record_id, action_text_rich_texts.record_type, action_text_rich_texts.updated_at";

    public const string Blobs =
        "active_storage_blobs.id, active_storage_blobs.byte_size, active_storage_blobs.checksum, active_storage_blobs.content_type, " +
        "active_storage_blobs.created_at, active_storage_blobs.filename, active_storage_blobs.\"key\", active_storage_blobs.metadata, " +
        "active_storage_blobs.service_name";

    public const string Attachments =
        "active_storage_attachments.id, active_storage_attachments.blob_id, active_storage_attachments.created_at, " +
        "active_storage_attachments.name, active_storage_attachments.record_id, active_storage_attachments.record_type";

    /// <summary>
    /// Every list above by table. Three tables have none, because no query reads their whole rows:
    /// <c>schema_migrations</c> and <c>ar_internal_metadata</c>, which only <see cref="SchemaSetup"/> touches, and
    /// <c>active_storage_variant_records</c>, which the storage records look up by blob and digest for an id.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ByTable { get; } = new Dictionary<string, string>( StringComparer.Ordinal )
    {
        [TableNames.Accounts] = Accounts,
        [TableNames.Users] = Users,
        [TableNames.Bans] = Bans,
        [TableNames.Sessions] = Sessions,
        [TableNames.Searches] = Searches,
        [TableNames.PushSubscriptions] = PushSubscriptions,
        [TableNames.Webhooks] = Webhooks,
        [TableNames.Rooms] = Rooms,
        [TableNames.Memberships] = Memberships,
        [TableNames.Messages] = Messages,
        [TableNames.Boosts] = Boosts,
        [TableNames.RichTexts] = RichTexts,
        [TableNames.Blobs] = Blobs,
        [TableNames.Attachments] = Attachments,
    };
}
