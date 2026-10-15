using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>Uses an exact SQL name during native binding while preserving the caller's ADO.NET parameter name.</summary>
internal sealed class PreparedSqliteParameter : SqliteParameter
{
    private string? _bindingName;

    [AllowNull]
    public override string ParameterName
    {
        get => _bindingName ?? base.ParameterName;
        set
        {
            base.ParameterName = value;
            _bindingName = null;
        }
    }

    internal void BindName( SqliteParameterNames names ) => _bindingName = names.Find( base.ParameterName );

    internal void RestoreName( ) => _bindingName = null;
}
