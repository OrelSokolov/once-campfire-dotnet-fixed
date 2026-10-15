namespace Campfire.Web.Paths;

public static partial class ParamNames
{
    /// <summary>
    /// The route values endpoint handlers bind by name (<c>[FromRoute( Name = … )]</c>): the <c>{…}</c> tokens of the
    /// route templates, which keep Rails' snake_case.
    /// </summary>
    public static class RouteValues
    {
        public const string RoomId = Rooms.Id;
        public const string MessageId = "message_id";
        public const string UserId = "user_id";
        public const string BotId = "bot_id";
        public const string PushSubscriptionId = "push_subscription_id";
        public const string JoinCode = "join_code";
        public const string SignedId = "signed_id";
        public const string SignedBlobId = "signed_blob_id";
        public const string VariationKey = "variation_key";
        public const string EncodedKey = "encoded_key";
    }
}
