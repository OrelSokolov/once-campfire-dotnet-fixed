using Campfire.Web.Security;

namespace Campfire.Web.Paths;

public static partial class ParamNames
{
    /// <summary>Sign-in (<c>sessions#create</c>) and sign-out (<c>sessions#destroy</c>) fields.</summary>
    public static class Sessions
    {
        /// <summary><c>authenticate_by(email_address:, password:)</c> reads the user attributes' names.</summary>
        public const string EmailAddress = Users.EmailAddress;

        public const string Password = Users.Password;

        /// <summary>Where to go after signing in.</summary>
        public const string ReturnUrl = BrowserAuthentication.ReturnUrlParameter;

        /// <summary>The browser's push subscription, removed on sign-out.</summary>
        public const string PushSubscriptionEndpoint = "push_subscription_endpoint";
    }
}
