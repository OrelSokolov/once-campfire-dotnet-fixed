using Campfire.Application.Data;
using Campfire.Application.Data.Hooks;
using Campfire.Application.Data.Records;
using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Application.Users;

using Dapper;

namespace Campfire.Application.Bots;

/// <summary><c>User::Bot</c> (<c>app/models/user/bot.rb</c>) and the <c>webhooks</c> rows.</summary>
public static class BotOperations
{
    /// <summary><c>User.generate_bot_token</c>: <c>SecureRandom.alphanumeric(12)</c>.</summary>
    public static string GenerateBotToken( ) => SecureTokens.Alphanumeric( 12 );

    /// <summary>
    /// <c>User.create_bot!(name:, avatar:, webhook_url:)</c>: the bot user in one transaction (with the user's
    /// after-commit work), then, when a webhook URL was given (even blank), its webhook row in a second.
    /// </summary>
    public static User CreateBot(
        WriterLease lease, string name, string? webhookUrl, NewAttachment? avatar = null, SignedBlobAttachment? avatarBlob = null )
    {
        var bot = lease.Transaction( scope => UserOperations.Create( scope, new NewUser
        {
            Name = name,
            BotToken = GenerateBotToken( ),
            Role = UserRole.Bot,
            Avatar = avatar,
            AvatarBlob = avatarBlob,
        } ) );

        if ( webhookUrl is not null )
        {
            lease.Transaction( scope => CreateWebhook( scope, bot.Id, webhookUrl ) );
        }

        return bot;
    }

    /// <summary>
    /// <c>update_bot!(attributes)</c>: in one transaction, the webhook first (updated or created for a present URL,
    /// destroyed for a blank or missing one), then the user.
    /// </summary>
    public static User UpdateBot( WriteScope scope, long botId, UserChanges changes, string? webhookUrl )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var webhook = FindWebhookByBotId( db, botId );
        if ( !string.IsNullOrWhiteSpace( webhookUrl ) )
        {
            if ( webhook is null )
            {
                CreateWebhook( scope, botId, webhookUrl );
            }
            else if ( webhook.Url != webhookUrl )
            {
                webhook.Url = webhookUrl;
                webhook.UpdatedAt = scope.Now;
                DataWrites.ExpectOneRow(
                    db.Connection.Execute( "UPDATE webhooks SET url = @Url, updated_at = @UpdatedAt WHERE id = @Id", webhook, db.Transaction ),
                    RecordKeys.Webhook( webhook.Id ) );
            }
        }
        else if ( webhook is not null )
        {
            DataWrites.ExpectOneRow(
                db.Connection.Execute( "DELETE FROM webhooks WHERE id = @Id", webhook, db.Transaction ), RecordKeys.Webhook( webhook.Id ) );
        }

        return UserOperations.Update( scope, botId, changes );
    }

    /// <summary><c>reset_bot_key</c>: <c>update! bot_token: generate_bot_token</c>.</summary>
    public static User ResetBotKey( WriteScope scope, long botId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var bot = UserQueries.Find( scope.Db, botId );
        var original = bot.Copy( );
        scope.Enlist( RecordKeys.User( bot.Id ) );
        bot.BotToken = GenerateBotToken( );
        UserOperations.SaveIfChanged( scope, original, bot );
        return bot;
    }

    /// <summary><c>create_webhook!(url:)</c>.</summary>
    public static Webhook CreateWebhook( WriteScope scope, long botId, string? url )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var now = scope.Now;
        var webhook = new Webhook { UserId = botId, Url = url, CreatedAt = now, UpdatedAt = now };
        webhook.Id = DataWrites.Save( ( ) => scope.Db.Connection.ExecuteScalar<long>(
            "INSERT INTO webhooks (created_at, updated_at, url, user_id) VALUES (@CreatedAt, @UpdatedAt, @Url, @UserId) RETURNING id",
            webhook,
            scope.Db.Transaction ) );
        return webhook;
    }

    /// <summary><c>user.webhook</c>.</summary>
    public static Webhook? FindWebhookByBotId( CampfireDb db, long botId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Webhook>(
            $"SELECT {Columns.Webhooks} FROM webhooks WHERE webhooks.user_id = @botId LIMIT 1", new
            {
                botId
            }, db.Transaction );
    }

    /// <summary><c>deliver_webhook_later(message)</c>: enqueues <c>Bot::WebhookJob</c> only for a bot with a webhook.</summary>
    public static bool DeliverWebhookLater( CampfireDb db, IWebhookJobs jobs, long botId, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( jobs );

        if ( !db.Connection.ExecuteScalar<bool>( "SELECT EXISTS (SELECT 1 FROM webhooks WHERE webhooks.user_id = @botId)", new
        {
            botId
        }, db.Transaction ) )
        {
            return false;
        }

        jobs.DeliverLater( botId, messageId );
        return true;
    }

    /// <summary>
    /// <c>MessagesController#deliver_webhooks_to_bots</c>, after the create broadcast: a direct room's active bots, or
    /// else the mentioned active bots, never the creator; each with a webhook gets a delivery job.
    /// </summary>
    public static void DeliverWebhooksToBots( CampfireDb db, CampfireHooks hooks, Message message, Room room )
    {
        ArgumentNullException.ThrowIfNull( hooks );

        foreach ( var botId in MessageQueries.BotsEligibleForWebhook( db, hooks.BodyText, message, room ) )
        {
            DeliverWebhookLater( db, hooks.WebhookJobs, botId, message.Id );
        }
    }
}
