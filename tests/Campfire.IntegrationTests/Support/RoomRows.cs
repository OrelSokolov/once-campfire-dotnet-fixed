namespace Campfire.IntegrationTests.Support;

/// <summary>A room's row and its members as the room administration tests read them back, on side connections.</summary>
internal static class RoomRows
{
    /// <summary>The room's <c>type</c> (<c>Rooms::Closed</c>, …) and <c>name</c>.</summary>
    public static (string? Type, string? Name) TypeAndName( SliceAppFactory factory, long roomId )
    {
        ArgumentNullException.ThrowIfNull( factory );

        var type = factory.Scalar<string>( $"SELECT type FROM rooms WHERE id = {roomId}" );
        var name = factory.Scalar<string>( $"SELECT name FROM rooms WHERE id = {roomId}" );
        return (type, name);
    }

    /// <summary>The ids of the room's members, ascending and comma-separated; null when it has none.</summary>
    public static string? MemberIds( SliceAppFactory factory, long roomId )
    {
        ArgumentNullException.ThrowIfNull( factory );

        return factory.Scalar<string>( $"SELECT group_concat(user_id) FROM (SELECT user_id FROM memberships WHERE room_id = {roomId} ORDER BY user_id)" );
    }

    /// <summary>The fixture users' ids as <see cref="MemberIds"/> reads them: ascending and comma-separated.</summary>
    public static string IdList( params string[] users ) => string.Join( ',', users.Select( SliceAppFactory.Id ).Order( ) );
}
