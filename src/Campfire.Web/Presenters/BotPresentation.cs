using Campfire.Application.Bots;
using Campfire.Application.Data;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Http;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Presenters;

/// <summary>The bots page (<c>accounts/bots/*</c>): each active bot with its key and rooms, and the bot form.</summary>
public static class BotPresentation
{
    /// <summary>
    /// <c>@bots = User.active_bots.ordered</c>, each with <c>bot.bot_key</c> and <c>bot.rooms.without_directs.ordered</c>
    /// (named with <c>room_display_name</c>, which is a shared room's name). Two statements for the whole page.
    /// </summary>
    public static List<BotView> Index( CampfireApp app, CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( app );

        var bots = UserQueries.ActiveBotsOrdered( db );
        var rooms = BotRooms.SharedRoomsOrdered( db, [.. bots.Select( bot => bot.Id )] );
        return [.. bots.Select( bot => new BotView(
            UserPresentation.Summary( app, bot ),
            bot.BotKey,
            [.. rooms[bot.Id].Select( room => new BotRoomView( room.RoomId, RoomsHelper.RoomDisplayName( room.Name, direct: false, [], null ) ) )] ) )];
    }

    /// <summary>
    /// What <c>accounts/bots/_form</c> fills in for <paramref name="bot"/>: its name, <c>webhook&amp;.url</c>, and
    /// <c>image_tag bot.avatar</c>'s source when one is attached, the blob's redirect URL on
    /// <paramref name="baseUrl"/> (<c>polymorphic_url</c>).
    /// </summary>
    public static BotForm Form( CampfireApp app, CampfireDb db, User bot, string baseUrl )
    {
        ArgumentNullException.ThrowIfNull( app );
        ArgumentNullException.ThrowIfNull( bot );

        var webhook = BotOperations.FindWebhookByBotId( db, bot.Id );
        var avatar = BlobRecords.Attached( db, new AttachmentOwner( UserOperations.RecordType, bot.Id ), UserAvatars.AttachmentName );
        var avatarUrl = avatar is null ? null : baseUrl + StorageRoutes.BlobRedirectPath( app.StorageVerifier, avatar );
        return new BotForm( bot.Name, webhook?.Url, avatarUrl );
    }
}
