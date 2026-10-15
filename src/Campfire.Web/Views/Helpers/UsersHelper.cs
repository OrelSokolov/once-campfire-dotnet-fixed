using System.Globalization;
using System.Text;
using Campfire.Application.Users;
using Campfire.Web.Paths;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>UsersHelper</c>, <c>Users::AvatarsHelper</c>, <c>Users::FilterHelper</c>, <c>Users::ProfilesHelper</c>,
/// <c>Users::SidebarHelper</c>, <c>AccountsHelper</c> and the <c>User</c> presentation methods the views call
/// (Rust <c>helpers/users.rs</c>).
/// </summary>
public static class UsersHelper
{
    /// <summary><c>Users::AvatarsHelper::AVATAR_COLORS</c>.</summary>
    public static readonly IReadOnlyList<string> AvatarColors =
    [
        "#AF2E1B", "#CC6324", "#3B4B59", "#BFA07A", "#ED8008", "#ED3F1C", "#BF1B1B", "#736B1E", "#D07B53",
        "#736356", "#AD1D1D", "#BF7C2A", "#C09C6F", "#698F9C", "#7C956B", "#5D618F", "#3B3633", "#67695E",
    ];

    private const string SidebarActions =
        "presence:present@window->rooms-list#read read-rooms:read->rooms-list#read turbo:frame-load->rooms-list#loaded " +
        "refresh-room:visible@window->turbo-frame#reload";

    /// <summary>The sidebar's turbo frame, which every page with a sidebar loads <c>users/sidebars/show</c> into.</summary>
    private const string SidebarFrameId = "user_sidebar";

    /// <summary><c>avatar_background_color(user)</c>: <c>Zlib.crc32(user.to_param)</c> picks the color.</summary>
    public static string AvatarBackgroundColor( long userId )
    {
        var crc = Crc32( Encoding.ASCII.GetBytes( userId.ToString( CultureInfo.InvariantCulture ) ) );
        return AvatarColors[( int ) (crc % ( uint ) AvatarColors.Count)];
    }

    /// <summary>
    /// <c>User#initials</c>: <c>name.scan(/\b\w/).join</c>. Ruby's <c>\w</c> is ASCII-only while <c>\b</c> sees Unicode
    /// word characters, so "Élodie" contributes nothing.
    /// </summary>
    public static string Initials( string name )
    {
        ArgumentNullException.ThrowIfNull( name );

        var builder = new StringBuilder( );
        var previousIsWord = false;
        foreach ( var rune in name.EnumerateRunes( ) )
        {
            var isAsciiWord = rune.IsAscii && (char.IsAsciiLetterOrDigit( ( char ) rune.Value ) || rune.Value == '_');
            if ( isAsciiWord && !previousIsWord )
            {
                builder.Append( ( char ) rune.Value );
            }

            previousIsWord = Rune.IsLetterOrDigit( rune ) || rune.Value == '_' || IsWordMark( rune );
        }

        return builder.ToString( );
    }

    /// <summary><c>user.role</c> as Rails stores it.</summary>
    public static string RoleName( UserRole role ) => role switch
    {
        UserRole.Administrator => UserRoleNames.Administrator,
        UserRole.Bot => UserRoleNames.Bot,
        _ => UserRoleNames.Member,
    };

    /// <summary><c>User#title</c>: <c>[ name, bio ].compact_blank.join(" – ")</c>.</summary>
    public static string UserTitle( string name, string? bio )
    {
        var parts = new[] { name, bio }.Where( part => !string.IsNullOrWhiteSpace( part ) );
        return string.Join( " – ", parts );
    }

    /// <summary><c>avatar_tag(user, **options)</c>: a link to the user around their avatar; the options go to the image.</summary>
    public static SafeHtml AvatarTag( ViewContext viewContext, UserView user, TagAttributes? options = null )
    {
        ArgumentNullException.ThrowIfNull( user );

        // `image_tag path, aria: { hidden: "true" }, size: 48, **options`: an `aria:` option replaces the default
        // aria hash in its place.
        var image = new TagAttributes( );
        var callerAria = options?.Entries.Where( entry => entry.Key.StartsWith( "aria-", StringComparison.Ordinal ) ).ToList( ) ?? [];
        if ( callerAria.Count == 0 )
        {
            image.AriaHidden( );
        }

        foreach ( var (name, value) in callerAria )
        {
            image.Attribute( name, value );
        }

        image.Size( 48 ).Merge( options );
        var imageTag = AssetTagHelper.ImageTag( viewContext, user.AvatarPath, image );
        var link = new TagAttributes( ).Title( user.Title ).Class( "btn avatar" ).Data( "turbo_frame", "_top" );
        return UrlHelper.LinkTo( Routes.User( user.Id ), link, imageTag );
    }

    /// <summary><see cref="AvatarTag(ViewContext, UserView, TagAttributes?)"/> for a <see cref="UserSummary"/>.</summary>
    public static SafeHtml AvatarTag( ViewContext viewContext, UserSummary user, TagAttributes? options = null ) =>
        AvatarTag( viewContext, user.ToView( ), options );

    /// <summary><c>button_to_direct_room_with(user)</c>.</summary>
    public static SafeHtml ButtonToDirectRoomWith( ViewContext viewContext, long userId ) =>
        FormHelper.ButtonTo( viewContext, Routes.RoomsDirectsWithUsers( [userId] ), new TagAttributes( ).Class( "btn btn--primary full-width txt--large" ) )
            .Wrap( AssetTagHelper.ImageTag( viewContext, "messages.svg" ) );

    /// <summary><c>account_logo_tag(style:)</c>. A nil style leaves a trailing space in the class.</summary>
    public static SafeHtml AccountLogoTag( ViewContext viewContext, string? style = null )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        var image = AssetTagHelper.ImageTag( viewContext, viewContext.Account.LogoPath, new TagAttributes( ).Alt( "Account logo" ).Size( 300 ) );
        return TagHelper.ContentTag( "figure", new TagAttributes( ).Class( "account-logo avatar " + style ), image );
    }

    /// <summary><c>profile_form_submit_button</c>.</summary>
    public static SafeHtml ProfileFormSubmitButton( ViewContext viewContext )
    {
        var content = ApplicationHelper.IconWithScreenReaderLabel( viewContext, "check.svg", "Save changes" );
        return TagHelper.ContentTag( "button", new TagAttributes( ).Class( "btn btn--reversed center txt-large" ).Type( "submit" ), content );
    }

    /// <summary><c>sidebar_turbo_frame_tag(src:)</c> around <paramref name="content"/>.</summary>
    public static SafeHtml SidebarTurboFrameTag( string? sourceUrl, SafeHtml content ) =>
        TurboHelper.TurboFrameTag( SidebarFrameId, content, SidebarFrameAttributes( ), sourceUrl, "_top" );

    /// <summary>The opening tag of <c>sidebar_turbo_frame_tag do ... end</c>.</summary>
    public static SafeHtml SidebarTurboFrameTagOpen( string? sourceUrl = null ) =>
        TurboHelper.TurboFrameTagOpen( SidebarFrameId, SidebarFrameAttributes( ), sourceUrl, "_top" );

    /// <summary><c>sidebar_turbo_frame_tag</c>'s data attributes; the actions are html_safe in the reference so "->" isn't escaped.</summary>
    private static TagAttributes SidebarFrameAttributes( ) =>
        new TagAttributes( ).Data( "turbo_permanent", true ).Data( "controller", "rooms-list read-rooms turbo-frame" )
            .Data( "rooms_list_unread_class", "unread" ).Data( "action", SafeHtml.Raw( SidebarActions ) );

    /// <summary>The opening tag of <c>user_filter_menu_tag do ... end</c>.</summary>
    public static SafeHtml UserFilterMenuTagOpen( ) =>
        TagHelper.OpenTag(
            "menu",
            new TagAttributes( ).Class( "flex flex-column gap margin-none pad overflow-y constrain-height" ).Data( "controller", "filter" )
                .Data( "filter_active_class", "filter--active" ).Data( "filter_selected_class", "selected" ) );

    /// <summary><c>user_filter_search_tag</c>.</summary>
    public static SafeHtml UserFilterSearchTag( ) =>
        TagHelper.BuilderTag(
            "input",
            new TagAttributes( ).Type( "search" ).Id( "search" ).Attribute( "autocorrect", "off" ).Autocomplete( "off" ).Attribute( "data-1p-ignore", "true" )
                .Class( "input input--transparent full-width" ).Placeholder( "Filter…" ).Data( "action", "input->filter#filter" ) );

    /// <summary>
    /// <c>users/sidebars/rooms/_direct</c>'s label for a group:
    /// <c>members.map { |m| m.name.split(' ')[0, 3].map { |s| s[0].capitalize }.join }.to_sentence(two_words_connector: '+')</c>.
    /// </summary>
    public static string DirectMemberInitials( IEnumerable<UserSummary> members )
    {
        ArgumentNullException.ThrowIfNull( members );

        var initials = members.Select( member => FirstLetters( member.Name ) ).ToList( );
        return RubyText.ToSentence( initials, "+" );
    }

    /// <summary><c>name.split(' ')[0, 3].map { |s| s[0].capitalize }.join</c>.</summary>
    private static string FirstLetters( string name ) =>
        string.Concat( RubyText.SplitWords( name ).Take( 3 ).Select( part => RubyText.Capitalize( part.EnumerateRunes( ).First( ).ToString( ) ) ) );

    /// <summary>The bot curl command for posting messages (<c>accounts/bots/_bot</c>).</summary>
    public static string CurlTextLine( string url ) => $"curl -d 'Hello!' {url}";

    /// <summary>The bot curl command for posting attachments (<c>accounts/bots/_bot</c>).</summary>
    public static string CurlUploadLine( string url ) => $"curl -F \"attachment=@/path/to/file\" {url}";

    /// <summary>Zlib's CRC-32 (IEEE, reflected).</summary>
    internal static uint Crc32( ReadOnlySpan<byte> bytes )
    {
        var crc = 0xFFFF_FFFFu;
        foreach ( var value in bytes )
        {
            crc ^= value;
            for ( var bit = 0; bit < 8; bit++ )
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>Unicode combining marks and connector punctuation count as word characters for Onigmo's <c>\b</c>.</summary>
    private static bool IsWordMark( Rune rune )
    {
        var category = Rune.GetUnicodeCategory( rune );
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.ConnectorPunctuation;
    }
}
