namespace Campfire.Web.Paths;

public static partial class ParamNames
{
    /// <summary>The composer and edit forms (<c>params[:message]</c>), and bot posts' attachment.</summary>
    public static class Messages
    {
        /// <summary>The form's param key (<c>form_with model: @message</c>).</summary>
        public const string Key = "message";

        public const string Body = "body";
        public const string Attachment = "attachment";
        public const string ClientMessageId = "client_message_id";

        /// <summary>The page of messages before this message's id.</summary>
        public const string Before = "before";

        /// <summary>The page of messages after this message's id.</summary>
        public const string After = "after";
    }

    /// <summary>Boost forms (<c>params[:boost]</c>).</summary>
    public static class Boosts
    {
        /// <summary>The form's param key (<c>form_with model: Boost.new</c>).</summary>
        public const string Key = "boost";

        public const string Content = "content";

        /// <summary>The boost picker's hidden <c>boost[content]</c> field.</summary>
        public const string ContentField = Key + "[" + Content + "]";
    }
}
