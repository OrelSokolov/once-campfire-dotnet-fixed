using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Campfire.Application.Users;

/// <summary>Bot keys, <c>"#{id}-#{bot_token}"</c> (<see cref="User.BotKey"/>), as <c>User.authenticate_bot</c> splits them.</summary>
internal static class BotKeys
{
    /// <summary>
    /// <c>bot_id, bot_token = bot_key.split("-")</c>. The id casts as Active Model's integer type does (leading digits
    /// of a numeric-looking string); a key without an id or a token gives nothing.
    /// </summary>
    public static bool TryParse( string botKey, out long botId, [NotNullWhen( true )] out string? token )
    {
        botId = 0;
        token = null;

        // Ruby's String#split drops trailing empty fields.
        var parts = botKey.Split( '-' );
        if ( parts.Length < 2 || parts[1].Length == 0 || !TryCastId( parts[0], out botId ) )
        {
            return false;
        }

        token = parts[1];
        return true;
    }

    /// <summary><c>ActiveModel::Type::Integer</c> on a query value: <c>"12abc"</c> is 12, <c>"abc"</c> matches nothing.</summary>
    private static bool TryCastId( string text, out long id )
    {
        var trimmed = text.TrimStart( );
        var signLength = trimmed.StartsWith( '-' ) || trimmed.StartsWith( '+' ) ? 1 : 0;
        var digits = trimmed.Skip( signLength ).TakeWhile( char.IsAsciiDigit ).Count( );
        var number = trimmed[..(signLength + digits)];
        return long.TryParse( number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id ) && digits > 0;
    }
}
