namespace Campfire.Web.Tests.Cable.Support;

public sealed record TestUser( long Id, string Name, IReadOnlyList<long> RoomIds );
