using Campfire.Application.Bots;
using Campfire.Application.Data;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Users;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Accounts;

/// <summary><c>Accounts::BotsController</c> (<c>app/controllers/accounts/bots_controller.rb</c>): chat bot setup.</summary>
public static partial class BotsEndpoints
{
    /// <summary><c>User.active_bots.find(params[key])</c> (404 unless there is one).</summary>
    public static async Task<User> FindActiveBotAsync( RequestContext requestContext, long botId )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var database = requestContext.Database( );
        var bot = await database.ReadAsync( db => UserQueries.FindActiveBotById( db, botId ), requestContext.RequestAborted );
        if ( bot is null )
        {
            throw new RecordNotFoundException( ModelNames.User );
        }

        return bot;
    }

    /// <summary><c>@bots = User.active_bots.ordered</c>.</summary>
    private static async Task<FrontendResult> IndexAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var bots = await app.Database.ReadAsync( db => BotPresentation.Index( app, db ), requestContext.RequestAborted );

        return await Pages.PageAsync( requestContext, viewContext => Views.accounts.bots.index.Create( new BotsIndexPage( viewContext, bots ) ) );
    }

    /// <summary><c>@bot = User.active_bots.new</c>.</summary>
    private static Task<FrontendResult> NewAsync( RequestContext requestContext ) =>
        Pages.PageAsync( requestContext, viewContext => Views.accounts.bots.new_html.Create( new BotsNewPage( viewContext, new BotForm( ) ) ) );

    /// <summary>
    /// <c>User.create_bot! bot_params</c>: the bot in one transaction, then its webhook in a second when
    /// <c>webhook_url</c> was given (even blank); then <c>redirect_to account_bots_url</c>.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var permitted = BotParams( requestContext );
        var name = UserParams.Text( permitted, ParamNames.Users.Name ).Value;
        if ( name is null )
        {
            throw NotNullViolations.ForColumn( UserParams.NameColumn );
        }

        var webhookUrl = UserParams.Text( permitted, ParamNames.Users.WebhookUrl ).Value;
        using var avatar = await StageAvatarAsync( requestContext, permitted );

        using ( var lease = await app.Database.AcquireWriterAsync( requestContext.RequestAborted ) )
        {
            BotOperations.CreateBot( lease, name, webhookUrl, avatar.Attachment, avatar.SignedBlob );
        }

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.AccountBots ) );
    }

    private static async Task<FrontendResult> EditAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var bot = requestContext.SelectedUser( );
        var baseUrl = requestContext.UrlFor( "" );
        var form = await app.Database.ReadAsync( db => BotPresentation.Form( app, db, bot, baseUrl ), requestContext.RequestAborted );

        return await Pages.PageAsync( requestContext, viewContext => Views.accounts.bots.edit.Create( new BotsEditPage( viewContext, bot.Id, form ) ) );
    }

    /// <summary>
    /// <c>@bot.update_bot! bot_params</c>: in one transaction, the webhook (updated, created, or destroyed when
    /// <c>webhook_url</c> is blank or missing), then the bot; then <c>redirect_to account_bots_url</c>.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var bot = requestContext.SelectedUser( );
        var permitted = BotParams( requestContext );
        var changes = new UserChanges
        {
            Name = UserParams.NotNullString( permitted, ParamNames.Users.Name, UserParams.NameColumn ),
        };
        var webhookUrl = UserParams.Text( permitted, ParamNames.Users.WebhookUrl ).Value;
        using var avatar = await StageAvatarAsync( requestContext, permitted );
        var withAvatar = changes.WithAvatar( avatar );

        await app.Database.WriteAsync( scope =>
        {
            BotOperations.UpdateBot( scope, bot.Id, withAvatar, webhookUrl );
            if ( avatar.Deletes )
            {
                UserAvatars.Remove( scope, bot.Id );
            }
        }, requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.AccountBots ) );
    }

    /// <summary><c>@bot.deactivate</c>, then <c>redirect_to account_bots_url</c>.</summary>
    private static async Task<FrontendResult> DestroyAsync( RequestContext requestContext )
    {
        var bot = requestContext.SelectedUser( );
        var database = requestContext.Database( );
        await database.WriteAsync( scope => UserOperations.Deactivate( scope, bot.Id ), requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.AccountBots ) );
    }

    /// <summary><c>@bot = User.active_bots.find(params[:id])</c>.</summary>
    private static async ValueTask<FrontendResult?> SetBotAsync( RequestContext requestContext, long botId )
    {
        requestContext.SetCurrent( new SelectedUser( await FindActiveBotAsync( requestContext, botId ) ) );
        return null;
    }

    /// <summary><c>params.require(:user).permit(:name, :avatar, :webhook_url)</c>.</summary>
    private static InputObject BotParams( RequestContext requestContext ) =>
        UserParams.Permit( requestContext, ParamNames.Users.Name, ParamNames.Users.Avatar, ParamNames.Users.WebhookUrl );

    /// <summary>The bot form's <c>avatar=</c>, staged before the write.</summary>
    private static ValueTask<StagedAssignment> StageAvatarAsync( RequestContext requestContext, InputObject permitted ) =>
        StagedAssignment.StageAsync( requestContext.App( ), AttachmentAssignment.From( permitted, ParamNames.Users.Avatar ), requestContext.RequestAborted );
}
