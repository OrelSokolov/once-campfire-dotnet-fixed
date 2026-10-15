using System.Globalization;
using System.Text;

using Campfire.Application.Bots;
using Campfire.Application.Data;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Fixtures;

/// <summary>
/// The sliver of ERB the fixtures use: <c>&lt;%= N.&lt;unit&gt;.ago %&gt;</c>, <c>BCrypt::Password.create("…")</c>
/// assigned to a local and read back, and <c>User.generate_bot_token</c>. Anything else fails loudly.
/// </summary>
internal sealed class FixtureErb( DateTime now, int bcryptCost )
{
    private readonly Dictionary<string, string> _locals = new( StringComparer.Ordinal );

    private readonly Dictionary<string, string> _digests = new( StringComparer.Ordinal );

    public string Render( string source, string file )
    {
        var output = new StringBuilder( );
        var rest = source;
        while ( rest.IndexOf( "<%", StringComparison.Ordinal ) is var start and >= 0 )
        {
            output.Append( rest[..start] );
            var after = rest[(start + 2)..];
            var end = after.IndexOf( "%>", StringComparison.Ordinal );
            if ( end < 0 )
            {
                throw new InvalidDataException( $"{file}: unterminated ERB tag" );
            }

            var printing = after.StartsWith( '=' );
            var code = (printing ? after[1..end] : after[..end]).Trim( );
            var value = Evaluate( code, file );
            if ( printing )
            {
                output.Append( value );
            }

            // Without trim mode, `<% %>` on its own line leaves the newline.
            rest = after[(end + 2)..];
        }

        return output.Append( rest ).ToString( );
    }

    private string Evaluate( string code, string file )
    {
        var assignment = code.IndexOf( " = ", StringComparison.Ordinal );
        if ( assignment > 0 )
        {
            _locals[code[..assignment].Trim( )] = Evaluate( code[(assignment + 3)..].Trim( ), file );
            return "";
        }

        if ( _locals.TryGetValue( code, out var local ) )
        {
            return local;
        }

        const string PasswordCreate = "BCrypt::Password.create(";
        if ( code.StartsWith( PasswordCreate, StringComparison.Ordinal ) && code.EndsWith( ')' ) )
        {
            var password = code[PasswordCreate.Length..^1].Trim( ).Trim( '"' );
            if ( !_digests.TryGetValue( password, out var digest ) )
            {
                digest = Passwords.Digest( password, bcryptCost );
                _digests[password] = digest;
            }

            return digest;
        }

        if ( code == "User.generate_bot_token" )
        {
            return BotOperations.GenerateBotToken( );
        }

        if ( Ago( code ) is { } duration )
        {
            // TimeWithZone#to_s in UTC: whole seconds.
            var whole = new DateTime( now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc );
            return $"{RailsTimestamp.Format( whole - duration )} UTC";
        }

        throw new InvalidDataException( $"{file}: unsupported ERB in fixture: {code}" );
    }

    /// <summary><c>1.hour.ago</c>, <c>36.minutes.ago</c>, <c>2.days.ago</c>…</summary>
    private static TimeSpan? Ago( string code )
    {
        var parts = code.Split( '.' );
        if ( parts.Length != 3 || parts[2] != "ago" || !int.TryParse( parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count ) )
        {
            return null;
        }

        return parts[1].TrimEnd( 's' ) switch
        {
            "second" => TimeSpan.FromSeconds( count ),
            "minute" => TimeSpan.FromMinutes( count ),
            "hour" => TimeSpan.FromHours( count ),
            "day" => TimeSpan.FromDays( count ),
            "week" => TimeSpan.FromDays( 7 * count ),
            _ => null,
        };
    }
}
