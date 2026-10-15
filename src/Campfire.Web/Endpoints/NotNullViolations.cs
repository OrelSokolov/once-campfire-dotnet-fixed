using Campfire.Application.Storage;

namespace Campfire.Web.Endpoints;

/// <summary>
/// <c>ActiveRecord::NotNullViolation</c>: a save that assigns nil to a <c>NOT NULL</c> column fails (a 500, the
/// transaction rolled back). The endpoints raise it before anything is written.
/// </summary>
public static class NotNullViolations
{
    /// <summary>The failure for <paramref name="column"/> (<c>table.column</c>), worded as SQLite words it.</summary>
    public static NotNullViolationException ForColumn( string column ) => new( $"NOT NULL constraint failed: {column}" );
}
