using Campfire.Application.Data;

namespace Campfire.Application.Tests.Fixtures;

/// <summary>Turns one fixture into column values.</summary>
internal static class FixtureRows
{
    private static readonly Dictionary<string, (string Key, string Column)[]> BelongsTo = new( StringComparer.Ordinal )
    {
        ["boosts"] = [("message", "message_id"), ("booster", "booster_id")],
        ["memberships"] = [("room", "room_id"), ("user", "user_id")],
        ["messages"] = [("room", "room_id"), ("creator", "creator_id")],
        ["rooms"] = [("creator", "creator_id")],
        ["bans"] = [("user", "user_id")],
        ["searches"] = [("user", "user_id")],
        ["sessions"] = [("user", "user_id")],
        ["webhooks"] = [("user", "user_id")],
        ["push_subscriptions"] = [("user", "user_id")],
        ["active_storage_attachments"] = [("blob", "blob_id")],
        ["active_storage_variant_records"] = [("blob", "blob_id")],
    };

    private static readonly string[] TimestampColumns = ["created_at", "updated_at"];

    private static readonly Dictionary<string, string> Polymorphic = new( StringComparer.Ordinal )
    {
        ["action_text_rich_texts"] = "record",
        ["active_storage_attachments"] = "record",
    };

    public static SortedDictionary<string, object?> Values(
        string table, string label, List<(string Key, YamlScalar Value)> row, HashSet<string> columns, DateTime now )
    {
        var values = new SortedDictionary<string, object?>( StringComparer.Ordinal );
        foreach ( var (key, value) in row )
        {
            if ( BelongsTo.GetValueOrDefault( table )?.FirstOrDefault( candidate => candidate.Key == key ) is { Column: not null } association )
            {
                values[association.Column] = RailsFixtures.Identify( value.Text.TrimStart( ':' ) );
            }
            else if ( Polymorphic.GetValueOrDefault( table ) == key )
            {
                var (target, type) = SplitPolymorphic( value.Text, $"{table}.{label}.{key}" );
                values[$"{key}_id"] = RailsFixtures.Identify( target );
                values[$"{key}_type"] = type;
            }
            else
            {
                values[key] = Cast( table, key, value );
            }
        }

        values.TryAdd( "id", RailsFixtures.Identify( label ) );
        foreach ( var column in TimestampColumns.Where( columns.Contains ) )
        {
            values.TryAdd( column, RailsTimestamp.Format( now ) );
        }

        return values;
    }

    /// <summary>Enum names to stored values, datetimes to Rails' text, and YAML types to SQLite values.</summary>
    private static object? Cast( string table, string column, YamlScalar value )
    {
        if ( value.IsNull )
        {
            return null;
        }

        if ( Enum( table, column, value.Text ) is { } stored )
        {
            return stored;
        }

        if ( column.EndsWith( "_at", StringComparison.Ordinal ) && RailsTimestamp.TryParse( value.Text, out var time ) )
        {
            return RailsTimestamp.Format( time );
        }

        if ( value.Boolean is { } boolean )
        {
            return boolean ? 1L : 0L;
        }

        return value.Integer is { } integer ? integer : value.Text;
    }

    private static object? Enum( string table, string column, string name ) => (table, column) switch
    {
        ("users", "role" ) => name switch
        {
            "member" => 0L,
            "administrator" => 1L,
            "bot" => 2L,
            _ => null,
        },
        ("users", "status" ) => name switch
        {
            "active" => 0L,
            "deactivated" => 1L,
            "banned" => 2L,
            _ => null,
        },
        _ => null,
    };

    private static (string Label, string Type) SplitPolymorphic( string text, string where )
    {
        var open = text.LastIndexOf( " (", StringComparison.Ordinal );
        if ( open < 0 || !text.EndsWith( ')' ) )
        {
            throw new InvalidDataException( $"{where} needs a (Class)" );
        }

        return (text[..open].Trim( ).TrimStart( ':' ), text[(open + 2)..^1]);
    }
}
