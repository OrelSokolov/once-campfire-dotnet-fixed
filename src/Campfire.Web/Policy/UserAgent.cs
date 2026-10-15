namespace Campfire.Web.Policy;

/// <summary>
/// A port of the useragent gem (0.16.11), which Rails' <c>allow_browser</c> and platform_agent use to
/// read the User-Agent header: <see cref="Parse"/> splits the header into products, and the first of
/// <c>UserAgent::Browsers::ALL</c> whose <c>extend?</c> accepts them decides how <c>browser</c>,
/// <c>version</c>, <c>platform</c>, <c>os</c>, <c>bot?</c> and <c>mobile?</c> are answered. Where the gem
/// raises, the <c>*OrRaise</c> methods throw <see cref="UserAgentRaisedException"/>; the plain
/// properties fall back to nil. Ruby's <c>\d</c> and <c>\s</c> are ASCII-only, and so are the
/// hand-written matchers. Ported from Rust <c>crates/campfire/src/concerns/user_agent.rs</c> at
/// <c>3fe9f61</c>; checked against <c>vectors/campfire_user_agents.json</c>.
/// </summary>
public sealed partial class UserAgent
{
    private const string DefaultUserAgent = "Mozilla/4.0 (compatible)";

    private readonly BrowserKind _kind;
    private readonly List<Product> _products;

    private UserAgent( BrowserKind kind, List<Product> products )
    {
        _kind = kind;
        _products = products;
    }

    private enum BrowserKind
    {
        Base,
        Edge,
        InternetExplorer,
        Opera,
        WechatBrowser,
        Vivaldi,
        Chrome,
        ITunes,
        PlayStation,
        PodcastAddict,
        Webkit,
        Gecko,
        WindowsMediaPlayer,
        AppleCoreMedia,
        Libavformat,
    }

    /// <summary><c>UserAgent::Browsers::ALL</c>, in detection order.</summary>
    private static readonly BrowserKind[] DetectionOrder =
    [
        BrowserKind.Edge, BrowserKind.InternetExplorer, BrowserKind.Opera, BrowserKind.WechatBrowser, BrowserKind.Vivaldi,
        BrowserKind.Chrome, BrowserKind.ITunes, BrowserKind.PlayStation, BrowserKind.PodcastAddict, BrowserKind.Webkit,
        BrowserKind.Gecko, BrowserKind.WindowsMediaPlayer, BrowserKind.AppleCoreMedia, BrowserKind.Libavformat,
    ];

    /// <summary><c>browser</c>; nil (possible for unparseable strings and a bare PlayStation Vita) is <c>""</c>.</summary>
    public string Browser => Rescue( this, static agent => agent.BrowserOrRaise( ), rescued: null ) ?? "";

    /// <summary><c>version</c>; nil is an empty version.</summary>
    public UserAgentVersion Version => Rescue( this, static agent => agent.VersionOrRaise( ), rescued: null ) ?? UserAgentVersion.Empty;

    public string? Platform => Rescue( this, static agent => agent.PlatformOrRaise( ), rescued: null );

    public string? OperatingSystem => Rescue( this, static agent => agent.OperatingSystemOrRaise( ), rescued: null );

    /// <summary><c>bot?</c>.</summary>
    public bool IsBot
    {
        get
        {
            if ( Application( ) is not { } application )
            {
                return true;
            }

            return _products.Any( product => product.CommentContains( segment => segment.Contains( "bot", StringComparison.OrdinalIgnoreCase ) ) )
                || DetectProduct( "Chrome-Lighthouse" ) is not null
                || application.Name.Contains( "bot", StringComparison.Ordinal );
        }
    }

    /// <summary><c>mobile?</c>.</summary>
    public bool IsMobile => Rescue( this, static agent => agent.MobileOrRaise( ), rescued: false );

    /// <summary><c>UserAgent.parse</c>: blank strings parse as <c>Mozilla/4.0 (compatible)</c>.</summary>
    public static UserAgent Parse( string? userAgent )
    {
        var rest = RubyStrip( userAgent ?? "" ).Length == 0 ? DefaultUserAgent : userAgent!;
        var products = new List<Product>( );
        while ( MatchProduct( rest ) is { } matched )
        {
            products.Add( matched.Product );
            rest = RubyStrip( rest[matched.Length..] );
        }

        var kind = DetectionOrder.FirstOrDefault( candidate => Extends( candidate, products ), BrowserKind.Base );

        return new UserAgent( kind, products );
    }

    /// <summary>ActiveSupport's <c>present?</c> for strings (Unicode whitespace is blank).</summary>
    public static bool IsPresent( string? text ) => text is not null && !text.All( char.IsWhiteSpace );

    internal static bool IsRubySpace( char character ) => character is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    internal static string RubyStrip( string text ) => text.Trim( ['\0', ' ', '\t', '\n', '\v', '\f', '\r'] );

    /// <summary><c>String#split(separator)</c>: trailing empty fields are dropped.</summary>
    internal static List<string> RubySplit( string text, string separator )
    {
        var parts = text.Split( separator ).ToList( );
        while ( parts.Count > 0 && parts[^1].Length == 0 )
        {
            parts.RemoveAt( parts.Count - 1 );
        }

        return parts;
    }

    /// <summary>
    /// <paramref name="read"/> of <paramref name="state"/>, or <paramref name="rescued"/> where the gem raises. Pass a
    /// <c>static</c> lambda: it is cached, so a property read allocates no delegate.
    /// </summary>
    internal static TResult Rescue<TState, TResult>( TState state, Func<TState, TResult> read, TResult rescued )
    {
        try
        {
            return read( state );
        }
        catch ( UserAgentRaisedException )
        {
            return rescued;
        }
    }

    internal static UserAgentRaisedException Raised( ) => new( );

    /// <summary>
    /// <c>UserAgent::MATCHER</c> at the start of <paramref name="input"/>:
    /// <c>^['"]*([^/\s]+)/?([^\s,]*)(\s\(([^\)]*)\)|,gzip\(gfe\))?</c>.
    /// </summary>
    private static (int Length, Product Product)? MatchProduct( string input )
    {
        static bool IsProductChar( char character ) => character != '/' && !IsRubySpace( character );

        var quotes = input.TakeWhile( character => character is '\'' or '"' ).Count( );
        int start;
        if ( quotes < input.Length && IsProductChar( input[quotes] ) )
        {
            start = quotes;
        }
        else if ( quotes > 0 )
        {
            start = quotes - 1;
        }
        else
        {
            return null;
        }

        var position = start + 1;
        while ( position < input.Length && IsProductChar( input[position] ) )
        {
            position++;
        }

        var name = input[start..position];
        if ( position < input.Length && input[position] == '/' )
        {
            position++;
        }

        var versionStart = position;
        while ( position < input.Length && !IsRubySpace( input[position] ) && input[position] != ',' )
        {
            position++;
        }

        var version = input[versionStart..position];
        List<string>? comment = null;
        if ( position + 1 < input.Length && IsRubySpace( input[position] ) && input[position + 1] == '(' )
        {
            var close = input.IndexOf( ')', position + 2 );
            if ( close >= 0 )
            {
                comment = RubySplit( input[(position + 2)..close], "; " );
                position = close + 1;
            }
        }
        else if ( input.AsSpan( position ).StartsWith( ",gzip(gfe)", StringComparison.Ordinal ) )
        {
            position += 10;
        }

        return (position, new Product( name, new UserAgentVersion( version ), comment ));
    }

    /// <summary>Each browser class's <c>self.extend?(agent)</c>.</summary>
    private static bool Extends( BrowserKind kind, List<Product> products )
    {
        var first = products.Count > 0 ? products[0] : null;
        var last = products.Count > 0 ? products[^1] : null;
        bool HasProduct( string name ) => products.Any( product => product.Name == name );

        return kind switch
        {
            BrowserKind.Edge => last?.Name == "Edge",
            BrowserKind.InternetExplorer => ExtendsInternetExplorer( first ),
            BrowserKind.Opera => first?.Name == "Opera" || last?.Name == "OPR",
            BrowserKind.WechatBrowser => products.Any( product => product.Name.Contains( "micromessenger", StringComparison.OrdinalIgnoreCase ) ),
            BrowserKind.Vivaldi => HasProduct( "Vivaldi" ),
            BrowserKind.Chrome => HasProduct( "Chrome" ) || HasProduct( "CriOS" ),
            BrowserKind.ITunes => HasProduct( "iTunes" ),
            BrowserKind.PlayStation => ExtendsPlayStation( first ),
            BrowserKind.PodcastAddict => products is [{ Name: "Podcast" }, { Name: "Addict" }, { Name: "-" }, ..],
            BrowserKind.Webkit => products.Any( IsWebkitProduct ),
            BrowserKind.Gecko => first?.Name == "Mozilla",
            BrowserKind.WindowsMediaPlayer => ExtendsWindowsMediaPlayer( products, first ),
            BrowserKind.AppleCoreMedia => HasProduct( "AppleCoreMedia" ),
            BrowserKind.Libavformat => products.Any( product => product.Name == "Lavf" || (product.Name == "NSPlayer" && first?.Version.Text == "4.1.0.3856") ),
            _ => true,
        };
    }

    private static bool ExtendsInternetExplorer( Product? first ) =>
        first is { Comment: not null }
        && ((first.CommentAt( 1 )?.Contains( "MSIE", StringComparison.Ordinal ) ?? false) || TridentRv( first.JoinedComment ?? "" ));

    private static bool ExtendsPlayStation( Product? first ) =>
        first?.CommentAt( 0 ) is { } comment
        && (comment.Contains( "PLAYSTATION 3", StringComparison.Ordinal )
            || comment.Contains( "PlayStation Vita", StringComparison.Ordinal )
            || comment.Contains( "PlayStation 4", StringComparison.Ordinal ));

    /// <summary><c>Webkit.extend?</c>'s test of one product: AppleWebKit itself, or a WebKit version in its comment.</summary>
    private static bool IsWebkitProduct( Product product ) =>
        product.Name.Equals( "applewebkit", StringComparison.OrdinalIgnoreCase )
        || product.CommentContains( segment => WebkitCommentVersion( segment ) is not null );

    private static bool ExtendsWindowsMediaPlayer( List<Product> products, Product? first ) =>
        products.Any( product => product.Name is "NSPlayer" or "Windows-Media-Player" or "WMFSDK" )
        && first?.Version.Text is not ("4.1.0.3856" or "7.10.0.3059" or "7.0.0.1956");

    private Product? First => _products.Count > 0 ? _products[0] : null;

    private Product? Last => _products.Count > 0 ? _products[^1] : null;

    /// <summary><c>detect_product</c>: case-insensitive product name lookup (also what <c>respond_to?</c> uses).</summary>
    private Product? DetectProduct( string name ) => _products.FirstOrDefault( product => product.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// <c>application</c>: most classes use the first product; the WebKit-based ones the first product
    /// with a non-empty comment.
    /// </summary>
    private Product? Application( )
    {
        if ( _kind is BrowserKind.Chrome or BrowserKind.Vivaldi or BrowserKind.Webkit or BrowserKind.ITunes or BrowserKind.AppleCoreMedia )
        {
            return _products.FirstOrDefault( product => product.Comment is { Count: > 0 } );
        }

        return First;
    }

    private List<string>? ApplicationComment( ) => Application( )?.Comment;

    private UserAgentVersion? BaseVersion( ) => Application( )?.Version;

    /// <summary>One product: <c>name/version (comment; parts)</c>.</summary>
    private sealed class Product( string name, UserAgentVersion version, List<string>? comment )
    {
        public string Name { get; } = name;

        public UserAgentVersion Version { get; } = version;

        public List<string>? Comment { get; } = comment;

        public string? JoinedComment => Comment is null ? null : string.Join( "; ", Comment );

        public string? CommentAt( int index ) => Comment is not null && index < Comment.Count ? Comment[index] : null;

        public bool CommentContains( Func<string, bool> predicate ) => Comment?.Any( predicate ) ?? false;
    }
}
