using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Views.Models;

/// <summary>Every slice model carries the request's <see cref="ViewContext"/>.</summary>
public interface IViewModel
{
    ViewContext ViewContext { get; }
}

/// <summary>Which layout wraps a page: turbo-rails swaps in <c>layouts/turbo_rails/frame</c> for Turbo-Frame requests.</summary>
public enum LayoutKind
{
    Application,
    TurboFrame,
}

/// <summary>
/// Per-request state every page needs: what <c>ApplicationController</c>, the layout and the helpers read from
/// <c>Current</c>, <c>request</c>, <c>flash</c> and the session (Rust <c>ViewContext</c> at <c>3fe9f61</c>). Outside a
/// request (broadcasts) <see cref="IncludeCsrfTags"/> is false and no token fields render; cached fragments also
/// omit those fields (<see cref="ForFragment"/>).
/// </summary>
public sealed record ViewContext
{
    internal PageRecorder? Recorder { get; set; }
    public CurrentUserView? CurrentUser { get; init; }

    public required AccountSummary Account { get; init; }

    /// <summary>Keep empty compatibility tags on browser pages; broadcasts and cached fragments omit them.</summary>
    public bool IncludeCsrfTags { get; init; }

    public string? FlashNotice { get; init; }

    public string? FlashAlert { get; init; }

    /// <summary><c>ApplicationPlatform</c> facts derived from the user agent.</summary>
    public Platform Platform { get; init; } = Platform.Unknown;

    /// <summary><c>Rails.configuration.x.vapid.public_key</c>; <see langword="null"/> omits the meta tag's content.</summary>
    public string? VapidPublicKey { get; init; }

    /// <summary>Digested asset paths and the asset tags, supplied by the assets component.</summary>
    public required AssetTagsView Assets { get; init; }

    /// <summary>The account's custom CSS, if any (<c>custom_styles_tag</c>).</summary>
    public string? CustomStyles { get; init; }

    /// <summary><c>script_aware_action_cable_meta_tag</c>: script name plus the cable mount path.</summary>
    public string CableUrl { get; init; } = "/cable";

    /// <summary><c>request.base_url</c> (<c>http://campfire.test</c>), for the <c>*_url</c> helpers on live pages.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// The configured public origin for URLs inside cached fragments, which must not come from a request's
    /// <c>Host</c>.
    /// <see langword="null"/> without one: fragments then use <see cref="BaseUrl"/>, as Rails does, and the origin in
    /// their keys keeps one request's <c>Host</c> out of the fragments other hosts read.
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary><c>request.url</c>, compared with the referrer by <c>link_back</c>.</summary>
    public string RequestUrl { get; init; } = string.Empty;

    /// <summary><c>request.referrer</c>.</summary>
    public string? Referrer { get; init; }

    /// <summary>The id of <c>last_room_visited</c>; <see langword="null"/> links back to the root.</summary>
    public long? LastRoomVisitedId { get; init; }

    /// <summary><c>Rails.application.config.app_version</c>.</summary>
    public string AppVersion { get; init; } = "0";

    public LayoutKind Layout { get; init; } = LayoutKind.Application;

    /// <summary>The process's fragment store; <see langword="null"/> renders uncached (<c>perform_caching = false</c>).</summary>
    public FragmentCache? Cache { get; init; }

    public string FragmentBaseUrl => PublicBaseUrl ?? BaseUrl;

    public bool CanAdminister => CurrentUser is { Administrator: true };

    public long? CurrentUserId => CurrentUser?.Id;

    /// <summary><c>Current.user == user</c>.</summary>
    public bool IsCurrentUser( long userId ) => CurrentUser?.Id == userId;

    /// <summary><c>root_url</c>, <c>session_transfer_url(...)</c>: the request's base URL and a path.</summary>
    public string Url( string path ) => BaseUrl + path;

    /// <summary><c>asset_path(logical)</c> through the assets component.</summary>
    public string Asset( string logicalPath ) => Assets.AssetPath( logicalPath );

    /// <summary>
    /// The context a cached fragment renders in: no token fields, the configured origin for URLs, and no viewer, so the fragment can be
    /// served to anyone.
    /// </summary>
    public ViewContext ForFragment( ) => this with
    {
        Recorder = null,
        IncludeCsrfTags = false,
        BaseUrl = FragmentBaseUrl,
        PublicBaseUrl = FragmentBaseUrl,
        CurrentUser = null,
        FlashNotice = null,
        FlashAlert = null,
        Referrer = null,
    };
}

/// <summary><c>Current.user</c> as the layout and helpers see it.</summary>
public sealed record CurrentUserView( long Id, string Name, bool Administrator, bool Bot, string AvatarPath );

/// <summary><c>Current.account</c> as the layout sees it.</summary>
/// <param name="Name">The account name.</param>
/// <param name="LogoPath"><c>fresh_account_logo_path</c> (no size).</param>
/// <param name="HasLogo"><c>Current.account.logo.attached?</c> (adds the <c>account-has-logo</c> body class).</param>
public sealed record AccountSummary( string Name, string LogoPath, bool HasLogo, bool RestrictRoomCreationToAdministrators = false )
{
    public string JoinCode { get; init; } = string.Empty;
}

/// <summary><c>ApplicationPlatform</c> facts derived from the user agent.</summary>
public sealed record Platform
{
    public static readonly Platform Unknown = new( );

    public bool Ios { get; init; }

    public bool Android { get; init; }

    public bool Mac { get; init; }

    public bool Windows { get; init; }

    public bool Chrome { get; init; }

    public bool Firefox { get; init; }

    public bool Safari { get; init; }

    public bool Edge { get; init; }

    public bool Mobile { get; init; }

    public bool Desktop { get; init; }

    /// <summary><c>ApplicationPlatform#apple_messages?</c>.</summary>
    public bool AppleMessages { get; init; }

    /// <summary><c>user_agent.browser</c> ("Chrome", "Safari", "Firefox", "Edge", ...).</summary>
    public string Browser { get; init; } = string.Empty;

    /// <summary><c>ApplicationPlatform#operating_system</c> ("macOS", "Windows", "iPhone", ...).</summary>
    public string OperatingSystem { get; init; } = string.Empty;
}

/// <summary>
/// What the assets component supplies to the views: digested paths for logical asset names, and the layout's
/// <c>stylesheet_link_tag :all</c> and <c>javascript_importmap_tags</c> output (see
/// <see cref="Helpers.AssetTagHelper.StylesheetLinkTags"/> and <see cref="Helpers.AssetTagHelper.JavascriptImportmapTags"/>).
/// </summary>
public sealed record AssetTagsView( Func<string, string> AssetPath, SafeHtml StylesheetTags, SafeHtml ImportmapTags );
