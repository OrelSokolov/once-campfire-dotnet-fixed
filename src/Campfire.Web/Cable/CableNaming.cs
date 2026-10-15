using System.Text;
using Campfire.Application.Data;
using Campfire.Application.Security;

namespace Campfire.Web.Cable;

/// <summary>
/// Broadcasting names: <c>Channel::Naming#channel_name</c>, <c>Channel::Broadcasting#broadcasting_for</c>
/// and Turbo's <c>stream_name_from</c>. Callers pass each streamable already converted: a record
/// with <see cref="GlobalIdParam"/> (<c>to_gid_param</c>), a symbol or string as itself
/// (<c>to_param</c>). Ported from Rust <c>crates/cable/src/naming.rs</c>.
/// </summary>
public static class CableNaming
{
    /// <summary>
    /// <c>GlobalID#to_param</c>: the URL-safe, unpadded Base64 of <c>gid://app/Model/id</c>. Records
    /// use their STI class name, so an open room is <c>Rooms::Open</c>.
    /// </summary>
    public static string GlobalIdParam( GlobalId globalId )
    {
        ArgumentNullException.ThrowIfNull( globalId );

        return globalId.ToParam( );
    }

    /// <summary>
    /// A Campfire user's GlobalID param: the user in a stream name (<c>[ Current.user, :rooms ]</c>) and the connection
    /// identifier of <c>identified_by :current_user</c>.
    /// </summary>
    public static string UserGlobalIdParam( long userId ) => GlobalId.For( ModelNames.User, userId ).ToParam( );

    /// <summary>
    /// <c>ActionCable::Channel::Naming.channel_name</c>:
    /// <c>name.delete_suffix("Channel").gsub("::", ":").underscore</c>.
    /// </summary>
    public static string ChannelName( string className )
    {
        ArgumentNullException.ThrowIfNull( className );

        var name = className.EndsWith( "Channel", StringComparison.Ordinal ) ? className[..^"Channel".Length] : className;

        return Underscore( name.Replace( "::", ":", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// <c>Channel::Broadcasting.broadcasting_for</c>: the channel name followed by each
    /// broadcastable, joined with <c>:</c>. <c>stream_for @room</c> in <c>RoomChannel</c> streams
    /// from <c>room:&lt;gid param&gt;</c>.
    /// </summary>
    public static string BroadcastingFor( string className, params IEnumerable<string> broadcastables ) =>
        string.Join( ':', broadcastables.Prepend( ChannelName( className ) ) );

    /// <summary>
    /// <c>Turbo::Streams::StreamName#stream_name_from</c>: <c>turbo_stream_from @room, :messages</c>
    /// names the stream <c>&lt;room gid param&gt;:messages</c>.
    /// </summary>
    public static string StreamNameFrom( params IEnumerable<string> streamables ) => string.Join( ':', streamables );

    /// <summary>
    /// <c>ActiveSupport::Inflector.underscore</c> without acronym inflections (Campfire defines
    /// none): <c>([A-Z\d]+)([A-Z][a-z])</c> and <c>([a-z\d])([A-Z])</c> get an underscore, <c>-</c>
    /// becomes <c>_</c>, and everything is lowercased.
    /// </summary>
    internal static string Underscore( string word )
    {
        var chars = word.Replace( "::", "/", StringComparison.Ordinal );
        var output = new StringBuilder( chars.Length + 4 );
        for ( var index = 0; index < chars.Length; index++ )
        {
            var character = chars[index];
            if ( char.IsAsciiLetterUpper( character ) && index > 0 )
            {
                var previous = chars[index - 1];
                var lowerBefore = char.IsAsciiLetterLower( previous ) || char.IsAsciiDigit( previous );
                var acronymEnd = (char.IsAsciiLetterUpper( previous ) || char.IsAsciiDigit( previous ))
                    && index + 1 < chars.Length && char.IsAsciiLetterLower( chars[index + 1] );
                if ( lowerBefore || acronymEnd )
                {
                    output.Append( '_' );
                }
            }

            output.Append( character == '-' ? '_' : char.ToLowerInvariant( character ) );
        }

        return output.ToString( );
    }
}
