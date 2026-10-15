namespace Campfire.Web.Policy;

/// <summary><c>browser</c> and <c>version</c> for each browser class of the useragent gem.</summary>
public sealed partial class UserAgent
{
    /// <summary><c>browser</c>, throwing where the gem raises.</summary>
    public string? BrowserOrRaise( ) => _kind switch
    {
        BrowserKind.Base => Application( )?.Name,
        BrowserKind.Edge => "Edge",
        BrowserKind.InternetExplorer => "Internet Explorer",
        BrowserKind.Opera => "Opera",
        BrowserKind.WechatBrowser => "Wechat Browser",
        BrowserKind.Vivaldi => "Vivaldi",
        BrowserKind.Chrome => DetectProduct( "Iron" ) is not null ? "Iron" : "Chrome",
        BrowserKind.ITunes => "iTunes",
        BrowserKind.PlayStation => PlayStationBrowser( ),
        BrowserKind.PodcastAddict => "Podcast Addict",
        BrowserKind.Webkit => WebkitBrowser( ),
        BrowserKind.Gecko => GeckoBrowser( ),
        BrowserKind.WindowsMediaPlayer => "Windows Media Player",
        BrowserKind.AppleCoreMedia => "AppleCoreMedia",
        _ => "libavformat",
    };

    /// <summary><c>version</c>, throwing where the gem raises.</summary>
    public UserAgentVersion? VersionOrRaise( ) => _kind switch
    {
        BrowserKind.Base or BrowserKind.WindowsMediaPlayer or BrowserKind.AppleCoreMedia => BaseVersion( ),
        BrowserKind.Edge or BrowserKind.Vivaldi => Last?.Version,
        BrowserKind.InternetExplorer => new UserAgentVersion( IeVersion( Application( )?.JoinedComment ?? "" ) ?? "" ),
        BrowserKind.Opera => OperaVersion( ),
        BrowserKind.WechatBrowser => (DetectProduct( "MicroMessenger" ) ?? throw Raised( )).Version,
        BrowserKind.Chrome => (DetectProduct( "CriOs" ) ?? DetectProduct( "chrome" ) ?? throw Raised( )).Version,
        BrowserKind.ITunes => (DetectProduct( "iTunes" ) ?? throw Raised( )).Version,
        BrowserKind.PlayStation => PlayStationVersion( ),
        BrowserKind.PodcastAddict => null,
        BrowserKind.Webkit => WebkitVersion( ),
        BrowserKind.Gecko => GeckoVersion( ),
        _ => DetectProduct( "NSPlayer" ) is not null ? null : BaseVersion( ),
    };

    private string? PlayStationBrowser( )
    {
        if ( ApplicationComment( ) is not { Count: > 0 } comment )
        {
            return null;
        }

        var first = comment[0];
        if ( first.Contains( "PLAYSTATION 3", StringComparison.Ordinal ) )
        {
            return "PS3 Internet Browser";
        }

        if ( Last?.Name == "Silk" )
        {
            return "Silk";
        }

        return first.Contains( "PlayStation 4", StringComparison.Ordinal ) ? "PS4 Internet Browser" : null;
    }

    private string WebkitBrowser( )
    {
        if ( WebkitOs( ) is { } os && os.Contains( "Android", StringComparison.Ordinal ) )
        {
            return "Android";
        }

        return WebkitPlatform( ) == "BlackBerry" ? "BlackBerry" : "Safari";
    }

    private string GeckoBrowser( )
    {
        foreach ( var name in ( string[] ) ["PaleMoon", "Firefox", "Camino", "Iceweasel", "Seamonkey"] )
        {
            if ( DetectProduct( name ) is not null )
            {
                return name;
            }
        }

        return First?.Name ?? "";
    }

    private UserAgentVersion? GeckoVersion( )
    {
        var version = (DetectProduct( GeckoBrowser( ) ) ?? throw Raised( )).Version;

        return version.IsNil ? BaseVersion( ) : version;
    }

    /// <summary><c>/Opera Mini/ === application</c>: only the comment can hold a space.</summary>
    private bool OperaMini( ) => First?.JoinedComment?.Contains( "Opera Mini", StringComparison.Ordinal ) ?? false;

    private UserAgentVersion? OperaVersion( )
    {
        if ( OperaMini( ) )
        {
            // `rescue Version.new` covers a comment without an "Opera Mini/<version>".
            var comment = ApplicationComment( )?.FirstOrDefault( segment => segment.Contains( "Opera Mini", StringComparison.Ordinal ) );
            var version = comment is null ? null : CaptureAfter( comment, "Opera Mini/", character => char.IsAsciiDigit( character ) || character == '.' );
            return new UserAgentVersion( version ?? "" );
        }

        return DetectProduct( "Version" )?.Version ?? DetectProduct( "OPR" )?.Version ?? BaseVersion( );
    }

    private UserAgentVersion? PlayStationVersion( )
    {
        if ( PlayStationOs( ) is not { } os )
        {
            return null;
        }

        UserAgentVersion After( string marker ) => new( RubySplit( os, marker ) is { Count: > 0 } parts ? parts[^1] : "" );

        if ( PlayStationBrowser( ) == "Silk" )
        {
            return Last?.Version;
        }

        return PlayStationPlatform( ) switch
        {
            "PlayStation 3" => After( "PLAYSTATION 3 " ),
            "PlayStation 4" => After( "PlayStation 4 " ),
            "PlayStation Vita" => After( "PlayStation Vita " ),
            _ => null,
        };
    }

    private UserAgentVersion WebkitVersion( )
    {
        if ( DetectProduct( "Version" ) is { } version )
        {
            return version.Version;
        }

        if ( WebkitOs( ) is { } os
            && CaptureAfter( os, "iOS ", static character => char.IsAsciiDigit( character ) || character == '.' ) is { } ios
            && WebkitBrowser( ) == "Safari" )
        {
            return new UserAgentVersion( ios.Replace( '_', '.' ) );
        }

        return new UserAgentVersion( WebkitBuildVersion( Webkit( )?.Text ?? "" ) ?? "" );
    }

    /// <summary><c>Webkit#webkit.version</c>: the AppleWebKit product's version, or one from a comment.</summary>
    private UserAgentVersion? Webkit( )
    {
        if ( _products.FirstOrDefault( candidate => candidate.Name.Equals( "applewebkit", StringComparison.OrdinalIgnoreCase ) ) is { } product )
        {
            return product.Version;
        }

        var fromComment = _products
            .SelectMany( candidate => candidate.Comment ?? [] )
            .Select( WebkitCommentVersion )
            .FirstOrDefault( version => version is not null );

        return fromComment is null ? null : new UserAgentVersion( fromComment );
    }

    /// <summary><c>Webkit::BuildVersions</c>: Safari versions before Safari 3 reported only the WebKit build.</summary>
    private static string? WebkitBuildVersion( string build ) => build switch
    {
        "85.7" => "1.0",
        "85.8.5" or "85.8.2" => "1.0.3",
        "124" => "1.2",
        "125.2" => "1.2.2",
        "125.4" => "1.2.3",
        "125.5.5" or "125.5.6" or "125.5.7" => "1.2.4",
        "312.1.1" or "312.1" => "1.3",
        "312.5" or "312.5.1" or "312.5.2" => "1.3.1",
        "312.8" or "312.8.1" => "1.3.2",
        "412" or "412.6" or "412.6.2" => "2.0",
        "412.7" => "2.0.1",
        "416.11" or "416.12" => "2.0.2",
        "417.9" or "418" => "2.0.3",
        "418.8" or "418.9" or "418.9.1" or "419" => "2.0.4",
        "425.13" => "2.2",
        "534.52.7" => "5.1.2",
        _ => null,
    };
}
