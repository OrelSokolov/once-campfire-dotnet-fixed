using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Search;

/// <summary>A user's recent searches (<c>app/models/search.rb</c>, <c>SearchesController</c>).</summary>
public static class SearchOperations
{
    /// <summary>
    /// <c>Current.user.searches.record(query)</c>: <c>find_or_create_by(query:).touch</c>. Creating one trims the
    /// user's searches to the ten most recently updated (<c>after_create :trim_recent_searches</c>).
    /// </summary>
    public static RecentSearch Record( WriteScope scope, long userId, string query )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var search = db.Connection.QueryFirstOrDefault<RecentSearch>(
            $"SELECT {Columns.Searches} FROM searches WHERE searches.user_id = @userId AND searches.query = @query LIMIT 1",
            new
            {
                userId,
                query
            },
            db.Transaction );
        if ( search is null )
        {
            search = Insert( scope, userId, query );
            TrimRecentSearches( scope, userId );
        }

        var key = RecordKeys.Search( search.Id );
        scope.Enlist( key );

        // touch: updated_at alone, written only when the time moved.
        var touchedAt = scope.Now;
        if ( search.UpdatedAt != touchedAt )
        {
            search.UpdatedAt = touchedAt;
            DataWrites.ExpectOneRow( db.Connection.Execute( "UPDATE searches SET updated_at = @UpdatedAt WHERE id = @Id", search, db.Transaction ), key );
        }

        return search;
    }

    /// <summary><c>Current.user.searches.destroy_all</c> (<c>SearchesController#clear</c>).</summary>
    public static void Clear( WriteScope scope, long userId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        scope.Db.Connection.Execute( "DELETE FROM searches WHERE user_id = @userId", new
        {
            userId
        }, scope.Db.Transaction );
    }

    /// <summary><c>Current.user.searches.ordered</c>: most recently updated first.</summary>
    public static List<RecentSearch> Ordered( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<RecentSearch>(
                $"SELECT {Columns.Searches} FROM searches WHERE searches.user_id = @userId ORDER BY searches.updated_at DESC", new
                {
                    userId
                }, db.Transaction )
            .AsList( );
    }

    /// <summary><c>find_or_create_by</c>'s create: the new search row.</summary>
    private static RecentSearch Insert( WriteScope scope, long userId, string query )
    {
        var db = scope.Db;
        var now = scope.Now;
        var search = new RecentSearch { UserId = userId, Query = query, CreatedAt = now, UpdatedAt = now };
        search.Id = DataWrites.Save( ( ) => db.Connection.ExecuteScalar<long>(
            "INSERT INTO searches (created_at, query, updated_at, user_id) VALUES (@CreatedAt, @Query, @UpdatedAt, @UserId) RETURNING id",
            search,
            db.Transaction ) );
        return search;
    }

    /// <summary><c>user.searches.excluding(user.searches.ordered.limit(10)).destroy_all</c>.</summary>
    private static void TrimRecentSearches( WriteScope scope, long userId ) =>
        scope.Db.Connection.Execute(
            "DELETE FROM searches WHERE searches.user_id = @userId AND searches.id NOT IN " +
            "(SELECT kept.id FROM searches AS kept WHERE kept.user_id = @userId ORDER BY kept.updated_at DESC LIMIT @kept)",
            new
            {
                userId,
                kept = RecentSearch.Kept
            },
            scope.Db.Transaction );
}
