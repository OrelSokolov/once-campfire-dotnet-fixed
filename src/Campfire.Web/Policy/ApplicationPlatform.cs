namespace Campfire.Web.Policy;

/// <summary>
/// <c>ApplicationPlatform</c> (<c>app/models/application_platform.rb</c>, over platform_agent 1.0.1),
/// which <c>SetPlatform</c> exposes to views as <c>platform</c>. The <c>*OrRaise</c> predicates throw
/// where Ruby raises (a nil <c>user_agent.browser</c>); the plain ones answer false. Ported from Rust
/// <c>crates/campfire/src/concerns/platform.rs</c> at <c>3fe9f61</c>.
/// </summary>
public sealed class ApplicationPlatform
{
    private readonly string _userAgentString;
    private readonly UserAgent _userAgent;

    public ApplicationPlatform( string? userAgent )
    {
        // `match?` works on `user_agent_string.to_s`, and UserAgent.parse treats nil like "".
        _userAgentString = userAgent ?? "";
        _userAgent = UserAgent.Parse( _userAgentString );
    }

    public bool IsIos => Matches( "iPhone" ) || Matches( "iPad" );

    public bool IsAndroid => Matches( "Android" );

    public bool IsMac => Matches( "Macintosh" );

    public bool IsChrome => UserAgent.Rescue( this, static platform => platform.ChromeOrRaise( ), rescued: false );

    public bool IsFirefox => UserAgent.Rescue( this, static platform => platform.FirefoxOrRaise( ), rescued: false );

    public bool IsSafari => UserAgent.Rescue( this, static platform => platform.SafariOrRaise( ), rescued: false );

    public bool IsEdge => UserAgent.Rescue( this, static platform => platform.EdgeOrRaise( ), rescued: false );

    /// <summary>Apple Messages link previews claim to be both the Facebook and Twitter bots.</summary>
    public bool IsAppleMessages =>
        _userAgentString.Contains( "facebookexternalhit", StringComparison.OrdinalIgnoreCase )
        && _userAgentString.Contains( "twitterbot", StringComparison.OrdinalIgnoreCase );

    public bool IsMobile => IsIos || IsAndroid;

    public bool IsDesktop => !IsMobile;

    public bool IsWindows => UserAgent.Rescue( this, static platform => platform.WindowsOrRaise( ), rescued: false );

    /// <summary><c>operating_system</c>: nil when the gem's <c>os</c> is nil.</summary>
    public string? OperatingSystem => UserAgent.Rescue( this, static platform => platform.OperatingSystemOrRaise( ), rescued: null );

    /// <summary><c>browser</c> (delegated to the useragent gem); nil is <c>""</c>.</summary>
    public string Browser => _userAgent.Browser;

    /// <summary>The parsed User-Agent.</summary>
    public UserAgent UserAgent => _userAgent;

    public bool ChromeOrRaise( ) => BrowserMatches( "Chrome" );

    public bool FirefoxOrRaise( ) => BrowserMatches( "Firefox", "FxiOS" );

    public bool SafariOrRaise( ) => BrowserMatches( "Safari" );

    public bool EdgeOrRaise( ) => BrowserMatches( "Edg" );

    public bool WindowsOrRaise( ) => OperatingSystemOrRaise( ) == "Windows";

    public string? OperatingSystemOrRaise( )
    {
        var platform = _userAgent.PlatformOrRaise( ) ?? "";
        ReadOnlySpan<(string Needle, string Name)> platformNames =
        [
            ("Android", "Android"),
            ("iPad", "iPad"),
            ("iPhone", "iPhone"),
            ("Macintosh", "macOS"),
            ("Windows", "Windows"),
            ("CrOS", "ChromeOS"),
        ];
        foreach ( var (needle, name) in platformNames )
        {
            if ( platform.Contains( needle, StringComparison.Ordinal ) )
            {
                return name;
            }
        }

        if ( _userAgent.OperatingSystemOrRaise( ) is not { } os )
        {
            return null;
        }

        return os.Contains( "Linux", StringComparison.Ordinal ) ? "Linux" : os;
    }

    private bool Matches( string needle ) => _userAgentString.Contains( needle, StringComparison.Ordinal );

    private bool BrowserMatches( params ReadOnlySpan<string> needles )
    {
        var browser = _userAgent.BrowserOrRaise( ) ?? throw UserAgent.Raised( );
        foreach ( var needle in needles )
        {
            if ( browser.Contains( needle, StringComparison.Ordinal ) )
            {
                return true;
            }
        }

        return false;
    }
}
