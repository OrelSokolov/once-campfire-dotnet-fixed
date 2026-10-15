using Campfire.Application.Bots;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts.Bots;

/// <summary>
/// <c>Accounts::Bots::KeysController</c> (<c>app/controllers/accounts/bots/keys_controller.rb</c>): a new bot key, which
/// revokes the old one at once (<c>User.authenticate_bot</c> matches the stored token).
/// </summary>
public static partial class KeysEndpoints
{
    /// <summary><c>User.active_bots.find(params[:bot_id]).reset_bot_key</c>, then <c>redirect_to account_bots_url</c>.</summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext, long botId )
    {
        var bot = await BotsEndpoints.FindActiveBotAsync( requestContext, botId );
        var database = requestContext.Database( );
        await database.WriteAsync( scope => BotOperations.ResetBotKey( scope, bot.Id ), requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.AccountBots ) );
    }
}
