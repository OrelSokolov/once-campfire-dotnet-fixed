using System.Text;

namespace Campfire.Application.Data;

/// <summary>
/// The <c>UPDATE</c> an Active Record <c>save</c> runs: only the columns whose values changed since the row was read,
/// then <c>updated_at</c>, by id; nothing at all when nothing changed (Rails' partial updates and dirty tracking).
/// </summary>
/// <remarks>
/// Writing only changed columns matters beyond speed: an unchanged <c>datetime</c> column is left as stored, even in
/// a form Rails no longer writes (<see cref="RailsTimestamp.Parse"/> accepts several). The statement names each value
/// by its member on the row object passed with it, so the SQL varies only in which fixed columns it lists.
/// </remarks>
internal sealed class ChangedColumns( string table )
{
    private readonly List<(string Column, string Member)> _changed = [];

    /// <summary>Whether any column changed.</summary>
    public bool Any => _changed.Count > 0;

    /// <summary>Adds <paramref name="column"/>, bound to the row's <paramref name="member"/>, when its value changed.</summary>
    public void Compare<T>( string column, string member, T original, T current )
    {
        if ( !EqualityComparer<T>.Default.Equals( original, current ) )
        {
            _changed.Add( (column, member) );
        }
    }

    /// <summary>
    /// <c>UPDATE table SET changed = @Member, …, updated_at = @UpdatedAt WHERE id = @Id</c>. The caller sets the
    /// row's <c>UpdatedAt</c> first.
    /// </summary>
    public string UpdateSql( )
    {
        var sql = new StringBuilder( "UPDATE " ).Append( table ).Append( " SET " );
        foreach ( var (column, member) in _changed )
        {
            sql.Append( column ).Append( " = @" ).Append( member ).Append( ", " );
        }

        return sql.Append( "updated_at = @UpdatedAt WHERE id = @Id" ).ToString( );
    }
}
