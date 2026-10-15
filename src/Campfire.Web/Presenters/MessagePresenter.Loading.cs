using Campfire.Application.Data.Records;
using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Application.Users;
using Campfire.Web.Views.Caching;

namespace Campfire.Web.Presenters;

/// <summary>The rows a page of messages needs, loaded together and remembered for the presenter's lifetime.</summary>
public sealed partial class MessagePresenter
{
    /// <summary>Loads boosts and user versions first; full users are fetched together only if a fragment misses.</summary>
    private void Preload( IReadOnlyList<Message> messages )
    {
        var messageIds = messages.Select( message => message.Id ).ToList( );
        foreach ( var group in BoostOperations.ForMessages( _db, messageIds ).GroupBy( boost => boost.MessageId ) )
        {
            _boosts[group.Key] = [.. group];
        }

        foreach ( var id in messageIds.Where( id => !_boosts.ContainsKey( id ) ) )
        {
            _boosts[id] = [];
        }

        var creatorIds = messages.Select( message => message.CreatorId );
        var boosterIds = _boosts.Values.SelectMany( list => list ).Select( boost => boost.BoosterId );
        var userIds = creatorIds.Concat( boosterIds ).Distinct( ).Where( id => !_versions.ContainsKey( id ) ).ToList( );
        var versions = UserQueries.VersionsByIds( _db, userIds );
        foreach ( var id in userIds )
        {
            _versions[id] = new RecordVersion( id, versions.TryGetValue( id, out var updated ) ? UserPresentation.AsUtc( updated ) : default );
        }

        _pendingUserIds.AddRange( userIds );
    }

    /// <summary>The bodies and attachments of the messages whose fragments missed, in one query each.</summary>
    private void LoadBodies( IReadOnlyList<long> messageIds )
    {
        _bodies = MessageBodies.BodiesByIds( _db, messageIds );
        _attachments = MessageBodies.AttachmentsByIds( _db, messageIds );
    }

    /// <summary>The message's body: loaded with the page's misses, else on its own.</summary>
    private string? Body( long messageId )
    {
        if ( _bodies is not null && _bodies.TryGetValue( messageId, out var loaded ) )
        {
            return loaded;
        }

        return MessageBodies.Body( _db, messageId );
    }

    /// <summary>The message's attachment and its blob: loaded with the page's misses, else on its own.</summary>
    private (ActiveStorageAttachment Attachment, ActiveStorageBlob Blob)? MessageAttachment( long messageId )
    {
        if ( _attachments is not null && _attachments.TryGetValue( messageId, out var attached ) )
        {
            return attached;
        }

        return MessageBodies.Attachment( _db, messageId );
    }

    private List<Boost> Boosts( long messageId )
    {
        if ( !_boosts.TryGetValue( messageId, out var boosts ) )
        {
            boosts = BoostOperations.ForMessages( _db, [messageId] );
            _boosts[messageId] = boosts;
        }

        return boosts;
    }

    private User? User( long id )
    {
        if ( _pendingUserIds.Count > 0 )
        {
            // A cold or mixed page still loads its creators and boosters in one query, never one query per view.
            var users = UserQueries.FindByIds( _db, _pendingUserIds );
            foreach ( var pendingId in _pendingUserIds )
            {
                _users[pendingId] = users.GetValueOrDefault( pendingId );
            }

            _pendingUserIds.Clear( );
        }

        if ( !_users.TryGetValue( id, out var user ) )
        {
            user = UserQueries.FindById( _db, id );
            _users[id] = user;
        }

        return user;
    }

    private RecordVersion Version( long userId )
    {
        if ( _versions.TryGetValue( userId, out var version ) )
        {
            return version;
        }

        if ( User( userId ) is { } user )
        {
            version = new RecordVersion( user.Id, UserPresentation.AsUtc( user.UpdatedAt ) );
        }
        else
        {
            version = new RecordVersion( userId, default );
        }

        _versions[userId] = version;
        return version;
    }

    /// <summary><c>room_display_name(message.room, for_user: nil)</c>.</summary>
    private string RoomName( long roomId )
    {
        if ( !_roomNames.TryGetValue( roomId, out var name ) )
        {
            name = RoomQueries.FindById( _db, roomId ) is { } room ? RoomPresentation.DisplayName( _db, room, forUser: null ) : "";
            _roomNames[roomId] = name;
        }

        return name;
    }
}
