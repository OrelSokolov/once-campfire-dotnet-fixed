namespace Campfire.Web.Policy;

/// <summary><c>platform</c>, <c>os</c> and <c>mobile?</c> for each browser class of the useragent gem.</summary>
public sealed partial class UserAgent
{
    /// <summary><c>platform</c>, throwing where the gem raises.</summary>
    public string? PlatformOrRaise( )
    {
        var comment = ApplicationComment( );
        var first = comment is { Count: > 0 } ? comment[0] : null;

        return _kind switch
        {
            BrowserKind.Base or BrowserKind.Libavformat => null,
            BrowserKind.Edge or BrowserKind.InternetExplorer or BrowserKind.WindowsMediaPlayer => "Windows",
            BrowserKind.Opera or BrowserKind.AppleCoreMedia => OperaPlatform( comment, first ),
            BrowserKind.WechatBrowser => WechatPlatform( comment, first ),
            BrowserKind.Chrome or BrowserKind.Vivaldi => ChromePlatform( comment, first ),
            BrowserKind.Webkit or BrowserKind.ITunes => WebkitPlatform( ),
            BrowserKind.PlayStation => PlayStationPlatform( ),
            BrowserKind.PodcastAddict => (PodcastAddictOs( ) ?? throw Raised( )).Contains( "Android", StringComparison.Ordinal ) ? "Android" : null,
            _ => comment is null ? null : GeckoPlatform( first ),
        };
    }

    /// <summary><c>os</c>, throwing where the gem raises.</summary>
    public string? OperatingSystemOrRaise( ) => _kind switch
    {
        BrowserKind.Base or BrowserKind.Libavformat => null,
        BrowserKind.Edge => NormalizeOs( FirstCommentWindowsOs( ) ?? "" ),
        BrowserKind.InternetExplorer => NormalizeOs( WindowsOs( Application( )?.JoinedComment ?? "" ) ?? "" ),
        BrowserKind.Opera => OperaOs( ),
        BrowserKind.WechatBrowser or BrowserKind.Chrome or BrowserKind.Vivaldi or BrowserKind.AppleCoreMedia => ApplicationChromeOs( ),
        BrowserKind.Webkit => WebkitOs( ),
        BrowserKind.ITunes => ITunesOs( ),
        BrowserKind.PlayStation => PlayStationOs( ),
        BrowserKind.PodcastAddict => PodcastAddictOs( ),
        BrowserKind.Gecko => GeckoOs( ),
        _ => WindowsMediaPlayerOs( ),
    };

    /// <summary><c>mobile?</c>, throwing where the gem raises.</summary>
    public bool MobileOrRaise( ) => _kind switch
    {
        BrowserKind.Opera => OperaMini( ),
        BrowserKind.PlayStation => PlayStationPlatform( ) == "PlayStation Vita",
        BrowserKind.PodcastAddict => true,
        BrowserKind.WindowsMediaPlayer => WindowsMediaPlayerOs( ) is "Windows Phone 8" or "Windows Phone 8.1",
        _ => DetectProduct( "Mobile" ) is not null
            || _products.Any( product => product.CommentContains( segment => segment == "Mobile" ) )
            || (OperatingSystemOrRaise( )?.Contains( "Android", StringComparison.Ordinal ) ?? false)
            || (ApplicationComment( )?.Any( segment => segment.StartsWith( "IEMobile", StringComparison.Ordinal ) ) ?? false),
    };

    /// <summary>Edge's <c>os</c>: the first comment segment, of any product, that names a Windows version.</summary>
    private string? FirstCommentWindowsOs( ) =>
        _products.SelectMany( product => product.Comment ?? [] ).Select( WindowsOs ).FirstOrDefault( os => os is not null );

    private string? ApplicationChromeOs( ) => ApplicationComment( ) is { } comment ? ChromeOs( comment ) : null;

    private static bool Contains( string? text, string needle ) => text?.Contains( needle, StringComparison.Ordinal ) ?? false;

    private static bool CommentContains( List<string> comment, string needle ) =>
        comment.Any( segment => segment.Contains( needle, StringComparison.Ordinal ) );

    /// <summary>Opera's and AppleCoreMedia's <c>platform</c>: Windows when the first comment segment says so, else that segment.</summary>
    private static string? OperaPlatform( List<string>? comment, string? first )
    {
        if ( comment is null )
        {
            return null;
        }

        return Contains( first, "Windows" ) ? "Windows" : first;
    }

    /// <summary>WeChat's <c>platform</c>: iPhone, then Android, else the first comment segment.</summary>
    private static string? WechatPlatform( List<string>? comment, string? first )
    {
        if ( comment is null )
        {
            return null;
        }

        if ( Contains( first, "iPhone" ) )
        {
            return "iPhone";
        }

        return CommentContains( comment, "Android" ) ? "Android" : first;
    }

    /// <summary>Chrome's and Vivaldi's <c>platform</c>: Windows, then ChromeOS, then Android, else the first comment segment.</summary>
    private static string? ChromePlatform( List<string>? comment, string? first )
    {
        if ( comment is null )
        {
            return null;
        }

        if ( Contains( first, "Windows" ) )
        {
            return "Windows";
        }

        if ( CommentContains( comment, "CrOS" ) )
        {
            return "ChromeOS";
        }

        return CommentContains( comment, "Android" ) ? "Android" : first;
    }

    private static string? GeckoPlatform( string? first ) => first switch
    {
        "compatible" or "Mobile" => null,
        { } windows when windows.StartsWith( "Windows ", StringComparison.Ordinal ) => "Windows",
        _ => first,
    };

    private string? WebkitPlatform( )
    {
        if ( ApplicationComment( ) is not { } comment )
        {
            return null;
        }

        var first = comment.Count > 0 ? comment[0] : null;
        if ( Contains( first, "Windows" ) )
        {
            return "Windows";
        }

        if ( first == "BB10" )
        {
            return "BlackBerry";
        }

        return comment.Any( segment => segment.Contains( "Android", StringComparison.Ordinal ) ) ? "Android" : first;
    }

    private string? PlayStationPlatform( )
    {
        var os = PlayStationOs( );

        return os switch
        {
            null => null,
            _ when os.Contains( "PLAYSTATION 3", StringComparison.Ordinal ) => "PlayStation 3",
            _ when os.Contains( "PlayStation 4", StringComparison.Ordinal ) => "PlayStation 4",
            _ when os.Contains( "PlayStation Vita", StringComparison.Ordinal ) => "PlayStation Vita",
            _ => null,
        };
    }

    private string? OperaOs( )
    {
        if ( ApplicationComment( ) is not { } comment )
        {
            return null;
        }

        return comment.Count > 0 && comment[0].Contains( "Windows", StringComparison.Ordinal ) ? NormalizeOs( comment[0] ) : comment.ElementAtOrDefault( 1 );
    }

    private string? WebkitOs( )
    {
        if ( ApplicationComment( ) is not { } comment )
        {
            return null;
        }

        string? CommentAt( int index ) => comment.ElementAtOrDefault( index );

        if ( Contains( CommentAt( 0 ), "Windows NT" ) )
        {
            return NormalizeOs( CommentAt( 0 )! );
        }

        if ( CommentAt( 2 ) is null || Contains( CommentAt( 1 ), "Android" ) )
        {
            return CommentAt( 1 ) is { } second ? NormalizeOs( second ) : null;
        }

        if ( comment.FirstOrDefault( segment => IosVersion( segment ) is not null ) is { } ios )
        {
            return NormalizeOs( ios );
        }

        return NormalizeOs( CommentAt( 2 )! );
    }

    private string? ITunesOs( )
    {
        var windows = ApplicationComment( ) is { Count: > 0 } comment && comment[0].Contains( "Windows", StringComparison.Ordinal );
        if ( !windows )
        {
            return WebkitOs( );
        }

        var fullOs = ITunesFullOs( ) ?? "";
        foreach ( var name in ( string[] ) ["Windows 8.1", "Windows 8", "Windows 7", "Windows Vista", "Windows XP"] )
        {
            if ( fullOs.Contains( name, StringComparison.Ordinal ) )
            {
                return name;
            }
        }

        return "Windows";
    }

    /// <summary><c>ITunes#full_os</c>: the comment was cut at the first ")", so "(Build 7601" gets it back.</summary>
    private string? ITunesFullOs( )
    {
        if ( ApplicationComment( ) is not { Count: > 1 } comment )
        {
            return null;
        }

        var fullOs = comment[1];
        var length = fullOs.Length;
        var reopened = length >= 11 && fullOs.AsSpan( length - 11, 7 ).SequenceEqual( "(Build " ) && fullOs[(length - 4)..].All( char.IsAsciiDigit );

        return reopened ? fullOs + ")" : fullOs;
    }

    private string? PlayStationOs( ) => ApplicationComment( ) is { } comment ? string.Join( " ", comment ) : null;

    /// <summary><c>PodcastAddict#os</c>; throws when the gem raises on a comment-less Dalvik/Mozilla.</summary>
    private string? PodcastAddictOs( )
    {
        if ( _products.Count < 4 || _products[3].Name is not ("Dalvik" or "Mozilla") )
        {
            return null;
        }

        var comment = _products[3].Comment ?? throw Raised( );

        return comment.Count switch
        {
            > 3 => comment[2],
            3 => "Android",
            _ => null,
        };
    }

    private string? GeckoOs( )
    {
        if ( ApplicationComment( ) is not { } comment )
        {
            return null;
        }

        var first = comment.ElementAtOrDefault( 0 );
        int index;
        if ( comment.ElementAtOrDefault( 1 ) == "U" )
        {
            index = 2;
        }
        else if ( first is not null && (first.StartsWith( "Windows ", StringComparison.Ordinal ) || first.StartsWith( "Android", StringComparison.Ordinal )) )
        {
            index = 0;
        }
        else if ( first == "Mobile" )
        {
            return null;
        }
        else
        {
            index = 1;
        }

        return comment.ElementAtOrDefault( index ) is { } os ? NormalizeOs( os ) : null;
    }

    /// <summary><c>version.to_a[0]</c> compared with an Integer: nil raises NoMethodError, a String ArgumentError.</summary>
    private ulong WindowsMediaPlayerMajor( )
    {
        var version = BaseVersion( ) ?? throw Raised( );

        return version.Segments.Count > 0 && version.Segments[0].IsNumber ? version.Segments[0].AsNumber( ) ?? ulong.MaxValue : throw Raised( );
    }

    private string WindowsMediaPlayerOs( )
    {
        var major = WindowsMediaPlayerMajor( );
        var version = BaseVersion( ) ?? UserAgentVersion.Empty;
        ulong? Part( int index ) => index < version.Segments.Count ? version.Segments[index].AsNumber( ) : null;

        return major switch
        {
            <= 4 => Part( 3 ) switch
            {
                3564 or 3925 => "Windows 98",
                3857 => "Windows 9x",
                3936 => "Windows XP",
                3938 => "Windows 2000",
                _ => "Windows",
            },
            7 => Part( 3 ) == 3055 ? "Windows 98" : "Windows",
            8 => "Windows XP",
            9 or 10 => Part( 3 ) switch
            {
                2980 => "Windows 98/2000",
                3268 or 3367 or 3270 => "Windows 2000",
                3802 or 4503 => "Windows XP",
                _ => "Windows",
            },
            11 or 12 => WindowsMediaPlayer11Os( Part( 2 ) ),
            _ => "Windows",
        };
    }

    private static string WindowsMediaPlayer11Os( ulong? build ) => build switch
    {
        9841 or 9858 or 9860 or 9879 => "Windows 10",
        9651 => "Windows Phone 8.1",
        9600 => "Windows 8.1",
        9200 => "Windows 8",
        7600 or 7601 => "Windows 7",
        >= 6000 and <= 6002 => "Windows Vista",
        5721 => "Windows XP",
        _ => "Windows",
    };

    /// <summary><c>os</c> shared by Chrome, Vivaldi, WechatBrowser and AppleCoreMedia.</summary>
    private static string? ChromeOs( List<string> comment )
    {
        string? CommentAt( int index ) => comment.ElementAtOrDefault( index );

        var pick = Contains( CommentAt( 0 ), "Windows NT" ) ? CommentAt( 0 )
            : CommentAt( 2 ) is null || Contains( CommentAt( 1 ), "Android" ) ? CommentAt( 1 )
            : CommentAt( 2 );

        return pick is null ? null : NormalizeOs( pick );
    }
}
