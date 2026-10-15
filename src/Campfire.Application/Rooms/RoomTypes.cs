using Campfire.Application.Memberships;

namespace Campfire.Application.Rooms;

/// <summary>The class names stored in <c>rooms.type</c>.</summary>
public static class RoomTypes
{
    public const string OpenClassName = "Rooms::Open";

    public const string ClosedClassName = "Rooms::Closed";

    public const string DirectClassName = "Rooms::Direct";

    public static string ClassName( RoomType type ) => type switch
    {
        RoomType.Open => OpenClassName,
        RoomType.Closed => ClosedClassName,
        RoomType.Direct => DirectClassName,
        _ => throw new ArgumentOutOfRangeException( nameof( type ), type, "Unknown room type" ),
    };

    /// <exception cref="InvalidOperationException">The stored class name is not a Campfire room type.</exception>
    public static RoomType FromClassName( string className ) => className switch
    {
        OpenClassName => RoomType.Open,
        ClosedClassName => RoomType.Closed,
        DirectClassName => RoomType.Direct,
        _ => throw new InvalidOperationException( $"Unknown room type \"{className}\"" ),
    };

    public static Involvement DefaultInvolvement( RoomType type ) =>
        type == RoomType.Direct ? Involvement.Everything : Involvement.Mentions;
}
