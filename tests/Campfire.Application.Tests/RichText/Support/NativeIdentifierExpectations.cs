using System.Text.RegularExpressions;

namespace Campfire.Application.Tests.RichText.Support;

/// <summary>Explicit native-contract differences; the imported Rails corpus stays unchanged.</summary>
internal static partial class NativeIdentifierExpectations
{
    private static readonly HashSet<string> RejectedIdentifiers = new( StringComparer.Ordinal )
    {
        "sgid tampered user", "sgid rails 7 marshal", "sgid expired", "sgid cross purpose", "sgid urlsafe unpadded",
        "sgid not base64", "sgid base64 not json", "sgid json array", "sgid leading separator"
    };

    public static bool IsRejected( string name ) => RejectedIdentifiers.Contains( name );
    public static string Presentation( string name, string reference )
    {
        if ( IsRejected( name ) )
        {
            return "<div class=\"lexxy-content\">\n  <p>☒</p>\n</div>\n";
        }

        return name switch
        {
            // Invalid signatures cannot resurrect a user from the legacy payload.
            "fuzz 40" or "fuzz 202" => InvalidKevinMention( ).Replace( reference, "☒" ),
            "missing attachable" => "<div class=\"lexxy-content\">\n  <p>Before ☒ after</p>\n</div>\n",
            "trix figure attachment json comment" => "<div class=\"lexxy-content\">\n  <div></div>\n</div>\n",
            "fuzz 267" => "<div class=\"lexxy-content\">\n  <small href=\"https://example.com/a?b=1&amp;c=2\">☒</small>\n</div>\n",
            "fuzz 44" => Fuzz44Presentation,
            _ => reference
        };
    }

    public static string PlainText( string name, string reference ) => IsRejected( name ) ? "" : name switch
    {
        "fuzz 40" or "fuzz 66" or "fuzz 202" => reference.Replace( "@Kevin", "", StringComparison.Ordinal ),
        "fuzz 391" => reference.Replace( "@Jason", "", StringComparison.Ordinal ),
        "missing attachable" => "Before  after",
        "trix figure attachment json comment" => "",
        "fuzz 44" =>
            "“http://example.com/>javascript:xhello”\n\n\n\n]]>]]>x@y@Davidme@example.com&]]>www.example.com<\n\n" +
                "www.example.comme@example.com]]>www.example.comhttp://example.com/>שלוםhttps://example.com/x.שלום" +
                "<https://example.com/x.(https://example.com/(a))",
        "fuzz 267" =>
            "https://example.com/x.שלום(https://example.com/(a))me@example.com\n\nwww.example.com\n\n\na b\nhttp://example.com/>\n\n" +
                "(https://example.com/(a))https://x.com/dhh/status/1?s=2  שלום\na b\nwww.example.comwww.example.comjavascript:xhello&",
        _ => reference
    };

    public static string Editable( string name, string reference ) => IsRejected( name ) ? "(raised)" : name switch
    {
        "fuzz 40" or "fuzz 66" or "fuzz 202" or "fuzz 391" => "(raised)",
        // System.Text.Json encodes supplementary Unicode as UTF-16 escapes inside this JSON-valued attribute.
        "lexxy mention quote name" => reference.Replace( "🎉", "\\uD83C\\uDF89", StringComparison.Ordinal ),
        _ => reference
    };

    [GeneratedRegex( "<span class=\"mention\"><a title=\"Kevin – Ops\"[^<]*><img[^>]*></a> Kevin</span>" )]
    private static partial Regex InvalidKevinMention( );

    // Independently reviewed HTML for the malformed-signature case: missing placeholders preserve surrounding
    // safe markup and the genuine David mention, while scripts, event attributes and hidden template content vanish.
    private const string Fuzz44Presentation = "<div class=\"lexxy-content\">\n  <blockquote href=\"https://example.org/\"><a target=\"_blank\" " +
        "href=\"http://example.com/\">http://example.com/</a>☒&gt;<sup>javascript:xhello</sup></blockquote><p>☒</p><u>]]&gt;x@y<span class=\"mention\"><a " +
        "title=\"David – Founder\" class=\"btn avatar\" href=\"/users/1\"><img src=\"/users/eyJfcmFpbHMiOnsiZGF0YSI6MSwicHVyIjoidXNlci9hdmF0YXIifX0--023b1642" +
        "4a00933214f527b2dbc6cb54ffa966a5b4fa3375c0678891f4e168e1/avatar?v=20240102030405\" width=\"48\" height=\"48\"></a> David</span></u><table></table>" +
        "<u><mark><a target=\"_blank\" href=\"mailto:me@example.com\">me@example.com</a><ul>☒</ul><a target=\"_blank\" " +
        "href=\"http://www.example.comme@example.com\">www.example.comme@example.com</a><mark>]]&gt;</mark><h1><a target=\"_blank\" " +
        "href=\"http://www.example.comhttps://example.com/x.(https://example.com/(a))\">www.example.comhttps://example.com/x.(https://example.com/(a))</a>" +
        "</h1></mark></u>\n</div>\n";
}
