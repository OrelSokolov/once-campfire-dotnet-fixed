namespace Campfire.Web.Policy;

/// <summary><c>UserAgent::OperatingSystems</c> and the gem's regexps, as hand-written ASCII matchers.</summary>
public sealed partial class UserAgent
{
    /// <summary><c>UserAgent::OperatingSystems.normalize_os</c>.</summary>
    internal static string NormalizeOs( string os )
    {
        var windows = os switch
        {
            "Windows NT 10.0" => "Windows 10",
            "Windows NT 6.3" => "Windows 8.1",
            "Windows NT 6.2" => "Windows 8",
            "Windows NT 6.1" => "Windows 7",
            "Windows NT 6.0" => "Windows Vista",
            "Windows NT 5.2" => "Windows XP x64 Edition",
            "Windows NT 5.1" => "Windows XP",
            "Windows NT 5.01" => "Windows 2000, Service Pack 1 (SP1)",
            "Windows NT 5.0" => "Windows 2000",
            "Windows NT 4.0" => "Windows NT 4.0",
            "Windows 98" => "Windows 98",
            "Windows 95" => "Windows 95",
            "Windows CE" => "Windows CE",
            _ => null,
        };
        if ( windows is not null )
        {
            return windows;
        }

        if ( MacOsXVersion( os ) is { } mac )
        {
            return mac.Version is { } version ? $"OS X {version.Replace( '_', '.' )}" : "OS X";
        }

        if ( IosVersion( os ) is { } ios )
        {
            return $"iOS {ios.Replace( '_', '.' )}";
        }

        return ChromeOsVersion( os ) is { } chromeOs ? $"ChromeOS {chromeOs}" : os;
    }

    /// <summary>The length of <c>Intel Mac OS X</c> or <c>PPC Mac OS X</c> at the start of <paramref name="text"/>; -1 for neither.</summary>
    private static int MacOsXPrefixLength( ReadOnlySpan<char> text )
    {
        const string Intel = "Intel Mac OS X";
        const string PowerPc = "PPC Mac OS X";
        if ( text.StartsWith( Intel, StringComparison.Ordinal ) )
        {
            return Intel.Length;
        }

        if ( text.StartsWith( PowerPc, StringComparison.Ordinal ) )
        {
            return PowerPc.Length;
        }

        return -1;
    }

    /// <summary>The next occurrence of <paramref name="needle"/> from <paramref name="start"/> on, or -1.</summary>
    private static int OrdinalIndexOf( string text, string needle, int start ) => text.IndexOf( needle, start, StringComparison.Ordinal );

    /// <summary><c>/(?:Intel|PPC) Mac OS X\s*([0-9_\.]+)?/</c>: a match, with its optional capture.</summary>
    private static (string? Version, bool Matched)? MacOsXVersion( string os )
    {
        for ( var index = 0; index < os.Length; index++ )
        {
            var rest = os.AsSpan( index );
            var prefix = MacOsXPrefixLength( rest );
            if ( prefix < 0 )
            {
                continue;
            }

            var after = Skip( os, index + prefix, IsRubySpace );
            var end = Skip( os, after, character => char.IsAsciiDigit( character ) || character is '_' or '.' );

            return (end > after ? os[after..end] : null, true);
        }

        return null;
    }

    /// <summary><c>IOS_VERSION_REGEX = /CPU (?:iPhone |iPod )?OS ([\d_]+) like Mac OS X/</c>.</summary>
    internal static string? IosVersion( string os )
    {
        for ( var matchIndex = OrdinalIndexOf( os, "CPU ", 0 ); matchIndex >= 0; matchIndex = OrdinalIndexOf( os, "CPU ", matchIndex + 1 ) )
        {
            var rest = matchIndex + 4;
            foreach ( var device in ( string[] ) ["iPhone ", "iPod ", ""] )
            {
                if ( !os.AsSpan( rest ).StartsWith( device + "OS ", StringComparison.Ordinal ) )
                {
                    continue;
                }

                var start = rest + device.Length + 3;
                var end = Skip( os, start, character => char.IsAsciiDigit( character ) || character == '_' );
                if ( end > start && os.AsSpan( end ).StartsWith( " like Mac OS X", StringComparison.Ordinal ) )
                {
                    return os[start..end];
                }
            }
        }

        return null;
    }

    /// <summary><c>/CrOS\s([^\s]+)\s(\d+(\.\d+)*)/</c>: the second capture.</summary>
    private static string? ChromeOsVersion( string os )
    {
        for ( var matchIndex = OrdinalIndexOf( os, "CrOS", 0 ); matchIndex >= 0; matchIndex = OrdinalIndexOf( os, "CrOS", matchIndex + 1 ) )
        {
            var spaceIndex = matchIndex + 4;
            if ( spaceIndex >= os.Length || !IsRubySpace( os[spaceIndex] ) )
            {
                continue;
            }

            var wordEnd = Skip( os, spaceIndex + 1, character => !IsRubySpace( character ) );
            if ( wordEnd == spaceIndex + 1 || wordEnd >= os.Length || !IsRubySpace( os[wordEnd] ) )
            {
                continue;
            }

            var start = wordEnd + 1;
            var end = Skip( os, start, char.IsAsciiDigit );
            if ( end == start )
            {
                continue;
            }

            while ( end + 1 < os.Length && os[end] == '.' && char.IsAsciiDigit( os[end + 1] ) )
            {
                end = Skip( os, end + 1, char.IsAsciiDigit );
            }

            return os[start..end];
        }

        return null;
    }

    /// <summary><c>/Windows NT [\d\.]+|Windows Phone (OS )?[\d\.]+/</c>: the matched text.</summary>
    private static string? WindowsOs( string text )
    {
        for ( var index = 0; index < text.Length; index++ )
        {
            int tail;
            if ( text.AsSpan( index ).StartsWith( "Windows NT ", StringComparison.Ordinal ) )
            {
                tail = index + 11;
            }
            else if ( text.AsSpan( index ).StartsWith( "Windows Phone ", StringComparison.Ordinal ) )
            {
                tail = index + 14;
                var afterOs = tail + 3;
                if ( text.AsSpan( tail ).StartsWith( "OS ", StringComparison.Ordinal ) && Skip( text, afterOs, IsVersionChar ) > afterOs )
                {
                    tail = afterOs;
                }
            }
            else
            {
                continue;
            }

            var end = Skip( text, tail, IsVersionChar );
            if ( end > tail )
            {
                return text[index..end];
            }
        }

        return null;
    }

    /// <summary><c>joined_comment =~ /Trident.+rv:/</c>.</summary>
    private static bool TridentRv( string text )
    {
        for ( var matchIndex = OrdinalIndexOf( text, "Trident", 0 ); matchIndex >= 0; matchIndex = OrdinalIndexOf( text, "Trident", matchIndex + 1 ) )
        {
            var rest = text[(matchIndex + 7)..];
            var line = rest.Split( '\n' )[0];
            if ( line.Length > 1 && line.IndexOf( "rv:", 1, StringComparison.Ordinal ) >= 1 )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>joined_comment[/(MSIE\s|rv:)([\d\.]+)/, 2]</c>.</summary>
    private static string? IeVersion( string text )
    {
        for ( var index = 0; index < text.Length; index++ )
        {
            int tail;
            if ( text.AsSpan( index ).StartsWith( "MSIE", StringComparison.Ordinal ) && index + 4 < text.Length && IsRubySpace( text[index + 4] ) )
            {
                tail = index + 5;
            }
            else if ( text.AsSpan( index ).StartsWith( "rv:", StringComparison.Ordinal ) )
            {
                tail = index + 3;
            }
            else
            {
                continue;
            }

            var end = Skip( text, tail, IsVersionChar );
            if ( end > tail )
            {
                return text[tail..end];
            }
        }

        return null;
    }

    /// <summary>The capture of <c>/&lt;prefix&gt;([class]+)/</c> at its leftmost match.</summary>
    private static string? CaptureAfter( string text, string prefix, Func<char, bool> characterClass )
    {
        for ( var matchIndex = OrdinalIndexOf( text, prefix, 0 ); matchIndex >= 0; matchIndex = OrdinalIndexOf( text, prefix, matchIndex + 1 ) )
        {
            var start = matchIndex + prefix.Length;
            var end = Skip( text, start, characterClass );
            if ( end > start )
            {
                return text[start..end];
            }
        }

        return null;
    }

    /// <summary><c>WEBKIT_VERSION_REGEXP = /\A(?&lt;webkit&gt;AppleWebKit)\/(?&lt;version&gt;[\d\.]+)/i</c>: the version.</summary>
    private static string? WebkitCommentVersion( string comment )
    {
        if ( comment.Length < 12 || !comment.AsSpan( 0, 11 ).Equals( "AppleWebKit", StringComparison.OrdinalIgnoreCase ) || comment[11] != '/' )
        {
            return null;
        }

        var end = Skip( comment, 12, IsVersionChar );

        return end > 12 ? comment[12..end] : null;
    }

    private static bool IsVersionChar( char character ) => char.IsAsciiDigit( character ) || character == '.';

    /// <summary>The index after the run of characters from <paramref name="start"/> satisfying <paramref name="predicate"/>.</summary>
    private static int Skip( string text, int start, Func<char, bool> predicate )
    {
        var position = Math.Min( start, text.Length );
        while ( position < text.Length && predicate( text[position] ) )
        {
            position++;
        }

        return position;
    }
}
