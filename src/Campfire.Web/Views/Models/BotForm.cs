namespace Campfire.Web.Views.Models;

/// <summary>The fields <c>accounts/bots/_form</c> fills in.</summary>
/// <param name="Name">The bot's name.</param>
/// <param name="WebhookUrl">The bot's webhook URL.</param>
/// <param name="AvatarUrl"><c>url_for(bot.avatar)</c> when attached.</param>
public sealed record BotForm( string? Name = null, string? WebhookUrl = null, string? AvatarUrl = null );
