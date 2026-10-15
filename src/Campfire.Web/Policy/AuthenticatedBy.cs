namespace Campfire.Web.Policy;

/// <summary><c>authenticated_by</c> (<c>"".inquiry</c>, <c>"session"</c> or <c>"bot_key"</c>).</summary>
public enum AuthenticatedBy
{
    Nothing,
    Session,
    BotKey,
}
