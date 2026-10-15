using Campfire.Web.Paths;
using Campfire.Web.Views.Helpers;

namespace Campfire.Web.Views.Models;

/// <summary>A room's type, with the names Rails derives from its class.</summary>
public enum RoomKind
{
    Open,
    Closed,
    Direct,
}

/// <summary>The names and ids a <see cref="RoomKind"/> gives a room.</summary>
public static class RoomKinds
{
    /// <summary><c>Rooms::Open.model_name.param_key</c>, the stem of <c>dom_id(room)</c>.</summary>
    public static string ParamKey( this RoomKind kind ) => kind switch
    {
        RoomKind.Open => "rooms_open",
        RoomKind.Closed => "rooms_closed",
        _ => ParamNames.Rooms.DirectKey,
    };

    public static bool IsDirect( this RoomKind kind ) => kind == RoomKind.Direct;

    /// <summary><c>dom_id(room, prefix)</c>.</summary>
    public static string DomId( this RoomKind kind, long roomId, string? prefix = null ) => TurboHelper.DomId( kind.ParamKey( ), roomId, prefix );
}
