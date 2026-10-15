using Campfire.Application.Rooms;
using Campfire.Application.Users;
using Campfire.Web.App;

namespace Campfire.Web.Workers;

/// <summary>
/// <c>RemoveBannedContentJob#perform</c> → <c>User#remove_banned_content</c>: each of the user's messages is destroyed
/// (in its own transaction) and its removal broadcast to its room.
/// </summary>
public sealed class RemoveBannedContentHandler( CampfireApp app ) : IJobHandler<RemoveBannedContentJob>
{
    public async Task PerformAsync( RemoveBannedContentJob job, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( job );

        using var lease = await app.Database.AcquireWriterAsync( cancellationToken );
        UserBans.RemoveBannedContent( lease, job.UserId, message =>
        {
            // The stream names the room's STI class, read on the held connection between the transactions.
            if ( RoomQueries.FindById( lease.Db, message.RoomId ) is { } room )
            {
                app.Broadcasts.MessageRemoved( room, message.ClientMessageId );
            }
        } );
    }
}
