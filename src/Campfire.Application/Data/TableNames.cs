namespace Campfire.Application.Data;

/// <summary>
/// Table names, as record keys, <see cref="ChangedColumns"/>, schema checks and Rails cache keys
/// (<c>messages/12-20261015…</c>) spell them. SQL text spells them inline.
/// </summary>
public static class TableNames
{
    public const string Accounts = "accounts";
    public const string Users = "users";
    public const string Bans = "bans";
    public const string Sessions = "sessions";
    public const string Searches = "searches";
    public const string PushSubscriptions = "push_subscriptions";
    public const string Webhooks = "webhooks";
    public const string Rooms = "rooms";
    public const string Memberships = "memberships";
    public const string Messages = "messages";
    public const string Boosts = "boosts";

    /// <summary>The FTS5 table behind message search (<c>Message::Searchable</c>).</summary>
    public const string MessageSearchIndex = "message_search_index";

    public const string RichTexts = "action_text_rich_texts";
    public const string Blobs = "active_storage_blobs";
    public const string Attachments = "active_storage_attachments";
    public const string VariantRecords = "active_storage_variant_records";

    /// <summary>Rails' migration ledger; only <see cref="SchemaSetup"/> reads it.</summary>
    public const string SchemaMigrations = "schema_migrations";

    /// <summary>Rails' environment and schema checksum rows; only <see cref="SchemaSetup"/> writes it.</summary>
    public const string InternalMetadata = "ar_internal_metadata";
}
