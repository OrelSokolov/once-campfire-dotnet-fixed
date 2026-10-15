namespace Campfire.Web.Paths;

public static partial class ParamNames
{
    /// <summary>User forms (<c>params[:user]</c>): sign-up, first run, profile, account users and bots.</summary>
    public static class Users
    {
        /// <summary>The form's param key (<c>form_with model: @user</c>).</summary>
        public const string Key = "user";

        public const string Name = "name";
        public const string Avatar = "avatar";
        public const string EmailAddress = "email_address";
        public const string Password = "password";
        public const string Bio = "bio";
        public const string WebhookUrl = "webhook_url";

        /// <summary>The account users form's role (<c>administrator</c> or <c>member</c>).</summary>
        public const string Role = "role";

        /// <summary>The account users form's <c>user[role]</c> field.</summary>
        public const string RoleField = Key + "[" + Role + "]";
    }
}
