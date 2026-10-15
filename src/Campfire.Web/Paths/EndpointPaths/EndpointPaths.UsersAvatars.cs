namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class UsersAvatars
    {
        public const string Avatar = "/users/me/avatar";
        public const string UsersUserIdLongAvatar = "/users/{user_id:long}/avatar";
        public const string UsersUserIdAvatar = "/users/{user_id}/avatar";
        public const string AvatarPng = "/users/{user_id}/avatar.png";
        public const string AvatarSvg = "/users/{user_id}/avatar.svg";
    }
}
