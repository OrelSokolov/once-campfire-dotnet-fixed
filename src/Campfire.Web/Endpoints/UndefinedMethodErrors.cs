namespace Campfire.Web.Endpoints;

/// <summary>
/// The <c>NoMethodError</c> messages Rails raises when an action calls a method on a missing record, most often
/// <c>Current.user</c>, or on a scalar where it expected parameters.
/// </summary>
public static class UndefinedMethodErrors
{
    public const string IdForNil = "undefined method 'id' for nil";
    public const string RoomsForNil = "undefined method 'rooms' for nil";
    public const string CanAdministerForNil = "undefined method 'can_administer?' for nil";
    public const string MembershipsForNil = "undefined method 'memberships' for nil";
    public const string PushSubscriptionsForNil = "undefined method 'push_subscriptions' for nil";
    public const string SearchesForNil = "undefined method 'searches' for nil";
    public const string NameForNil = "undefined method 'name' for nil";
    public const string AdministratorForNil = "undefined method 'administrator?' for nil";
    public const string ReachableMessagesForNil = "undefined method 'reachable_messages' for nil";
    public const string AvatarForNil = "undefined method 'avatar' for nil";
    public const string BotKeyForNil = "undefined method 'bot_key' for nil";
    public const string PermitForString = "undefined method 'permit' for an instance of String";
}
