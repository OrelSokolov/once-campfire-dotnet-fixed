namespace Campfire.Web.Paths;

public static partial class ParamNames
{
    /// <summary>Room forms (<c>params[:room]</c>), member selection, involvement and room-scoped lookups.</summary>
    public static class Rooms
    {
        /// <summary>The param key every room form submits under.</summary>
        public const string Key = "room";

        /// <summary>The direct-message picker form's key (<c>form_with model: Rooms::Direct.new</c>).</summary>
        public const string DirectKey = "rooms_direct";

        /// <summary>A room id outside a room path: message routes and user autocompletion.</summary>
        public const string Id = "room_id";

        public const string Name = "name";

        /// <summary>The room form's <c>room[name]</c> field.</summary>
        public const string NameField = Key + "[" + Name + "]";

        public const string UserIds = "user_ids";

        /// <summary>The member checkboxes' array field, <c>user_ids[]</c>.</summary>
        public const string UserIdsField = UserIds + "[]";

        /// <summary>The direct-message picker's autocompletion text input.</summary>
        public const string UserIdsInput = "user_ids_input";

        public const string Involvement = "involvement";
    }
}
