using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>
/// Exact native parameter names for a retained statement. This avoids the provider's repeated prefix searches
/// without choosing a prefix for ambiguous names or interpreting SQL literals and comments as parameters.
/// Maps live only as long as their commands; the physical connection already bounds retained SQL and commands.
/// </summary>
internal sealed class SqliteParameterNames
{
    private static readonly ConditionalWeakTable<SqliteCommand, SqliteParameterNames> Commands = new( );
    private readonly Dictionary<string, string?> _names = new( StringComparer.Ordinal );

    private SqliteParameterNames( string sql )
    {
        for ( var offset = 0; offset < sql.Length; offset++ )
        {
            var current = sql[offset];
            if ( current is '\'' or '"' or '`' or '[' )
            {
                offset = QuotedEnd( sql, offset, current == '[' ? ']' : current );
            }
            else if ( current == '-' && offset + 1 < sql.Length && sql[offset + 1] == '-' )
            {
                var end = sql.IndexOf( '\n', offset + 2 );
                offset = end < 0 ? sql.Length : end;
            }
            else if ( current == '/' && offset + 1 < sql.Length && sql[offset + 1] == '*' )
            {
                var end = sql.IndexOf( "*/", offset + 2, StringComparison.Ordinal );
                offset = end < 0 ? sql.Length : end + 1;
            }
            else if ( current is '@' or '$' or ':' )
            {
                var end = ParameterEnd( sql, offset );
                // Tcl-style SQLite names can contain namespace and parenthesized suffixes. Leave
                // those statements entirely to the provider rather than recognizing a partial name.
                if ( end < sql.Length && (sql[end] == '(' || sql.AsSpan( end ).StartsWith( "::", StringComparison.Ordinal )) )
                {
                    _names.Clear( );
                    return;
                }

                Remember( sql, offset, end );
                offset = end - 1;
            }
        }
    }

    public static SqliteParameterNames For( SqliteCommand command ) =>
        Commands.GetValue( command, static value => new SqliteParameterNames( value.CommandText ) );

    public string? Find( string name ) => _names.GetValueOrDefault( name );

    private void Remember( string sql, int offset, int end )
    {
        if ( end == offset + 1 )
        {
            return;
        }

        var name = sql[(offset + 1)..end];
        var native = sql[offset..end];
        if ( _names.TryGetValue( name, out var previous ) && previous != native )
        {
            _names[name] = null; // Preserve the provider's ambiguity exception.
        }
        else
        {
            _names[name] = native;
        }
    }

    private static int ParameterEnd( string sql, int offset )
    {
        var end = offset + 1;
        while ( end < sql.Length && IsNameCharacter( sql[end] ) )
        {
            end++;
        }

        return end;
    }

    private static bool IsNameCharacter( char value ) => char.IsAsciiLetterOrDigit( value ) || value is '_' or '$' || value >= '\u0080';

    private static int QuotedEnd( string sql, int offset, char quote )
    {
        for ( offset++; offset < sql.Length; offset++ )
        {
            if ( sql[offset] != quote )
            {
                continue;
            }

            if ( quote != ']' && offset + 1 < sql.Length && sql[offset + 1] == quote )
            {
                offset++;
                continue;
            }

            return offset;
        }

        return sql.Length;
    }
}
