namespace Campfire.Web.Paths;

public static partial class ParamNames
{
    /// <summary>Account settings, logo and custom styles forms (<c>params[:account]</c>), and the logo URL.</summary>
    public static class Accounts
    {
        /// <summary>The form's param key (<c>form_with model: @account</c>).</summary>
        public const string Key = "account";

        public const string Name = "name";
        public const string Logo = "logo";
        public const string Settings = "settings";
        public const string RestrictRoomCreationToAdministrators = "restrict_room_creation_to_administrators";
        public const string CustomStyles = "custom_styles";

        /// <summary>The logo URL's size (<c>small</c> or omitted).</summary>
        public const string LogoSize = "size";
    }
}
