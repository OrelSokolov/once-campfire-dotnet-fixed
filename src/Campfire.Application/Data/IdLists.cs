using System.Globalization;

namespace Campfire.Application.Data;

/// <summary>
/// A list of ids as one SQL parameter: a JSON array that the statement expands with
/// <c>IN (SELECT value FROM json_each(@ids))</c>.
/// </summary>
/// <remarks>
/// One parameter keeps each statement's text fixed whatever the list's length, so there is no bind-variable limit
/// and the statement stays analyzable at build time. Rails writes the same ids inline (<c>IN (1, 2, 3)</c>); the rows
/// matched are the same.
/// </remarks>
internal static class IdLists
{
    /// <summary>The ids as a JSON array, e.g. <c>[1,2,3]</c>.</summary>
    public static string Json( IEnumerable<long> ids ) =>
        $"[{string.Join( ',', ids.Select( id => id.ToString( CultureInfo.InvariantCulture ) ) )}]";
}
