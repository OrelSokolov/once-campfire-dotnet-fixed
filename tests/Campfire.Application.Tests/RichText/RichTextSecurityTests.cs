using System.Text;
using Campfire.Application.RichText;
using Campfire.Application.Tests.RichText.Support;

namespace Campfire.Application.Tests.RichText;

/// <summary>
/// Security assertions that hold whatever the oracle says: over every corpus case (the port's output
/// and Rails' output with the documented security fixes applied), over adversarial bodies, and over malformed
/// SGIDs. Two implementations can agree on something unsafe, so Rails' outputs are checked too.
/// </summary>
public sealed class RichTextSecurityTests
{
    public static TheoryData<string> CaseNames => [.. RichTextCorpus.Cases.Select( RichTextCorpus.Name )];

    public static TheoryData<string> AdversarialBodies =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<svg onload=alert(1)><circle></circle></svg>",
        "<a href=\"jav&#x09;ascript:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a href=\"javascript&colon;alert(1)\">x</a>",
        "<a href=\" &#14;javascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<a href=\"vbscript:msgbox(1)\">x</a>",
        "<iframe src=\"javascript:alert(1)\"></iframe><iframe srcdoc=\"<script>alert(1)</script>\"></iframe>",
        "<object data=\"javascript:alert(1)\"></object><embed src=\"javascript:alert(1)\">",
        "<form action=\"javascript:alert(1)\"><button formaction=\"javascript:alert(1)\">x</button></form>",
        "<p style=\"background:url(javascript:alert(1))\">x</p><p style=\"color: expression(alert(1))\">y</p>",
        "<img src=\"x\" name=\"body\"><form name=\"cookie\"></form><p id=\"location\">x</p>",
        "<span data-controller=\"x\" data-action=\"click->x#y\">x</span>",
        "<svg><a xlink:href=\"javascript:alert(1)\"><text x=\"20\" y=\"20\">x</text></a></svg>",
        "<svg><foreignObject><img src=x onerror=alert(1)></foreignObject></svg>",
        "<math><mtext><img src=x onerror=alert(1)></mtext></math>",
        "<math><annotation-xml encoding=\"text/html\"><img src=x onerror=alert(1)></annotation-xml></math>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<svg></p><style><a id=\"</style><img src=1 onerror=alert(1)>\">",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript>",
        "<form><math><mtext></form><form><mglyph><style></math><img src onerror=alert(1)>",
        "<select><style></select><img src onerror=alert(1)></style></select>",
        "<title><img src=x onerror=alert(1)></title><textarea><img src=x onerror=alert(1)></textarea>",
        "<template><img src=x onerror=alert(1)></template>",
        "<p title=\"x> http://evil.test/ <img src=x onerror=alert(1)>\">hi</p>",
        "<p><abbr title=\"x> me@evil.test <img src=x onerror=alert(1)>\">hi</abbr></p>",
        "<a href=\"http://x.test/\" title=\"&quot;><img src=x onerror=alert(1)>\">http://y.test/</a>",
        "<action-text-attachment content-type=\"text/html\" content=\"&lt;img src=x onerror=alert(1)&gt;&lt;script&gt;alert(1)&lt;/script&gt;\">" +
            "</action-text-attachment>",
        "<action-text-attachment content-type=\"image/png\" url=\"javascript:alert(1)\" caption=\"&lt;img src=x onerror=alert(1)&gt;\">" +
            "</action-text-attachment>",
        "<action-text-attachment content-type=\"video/mp4\" url=\"javascript:alert(1)\" width=\"&quot;&gt;&lt;script&gt;\"></action-text-attachment>",
        "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" href=\"javascript:alert(1)\" url=\"javascript:alert(2)\" " +
            "filename=\"&lt;script&gt;\" caption=\"&lt;img onerror=x&gt;\"></action-text-attachment>",
        "<figure data-trix-attachment=\"{&quot;contentType&quot;:&quot;text/html&quot;,&quot;content&quot;:&quot;&lt;img src=x onerror=alert(1)&gt;&quot;}\">" +
            "</figure>",
        "<div><action-text-attachment presentation=\"gallery\" content-type=\"image/png\" url=\"/a.png\"></action-text-attachment><action-text-attachment " +
            "presentation=\"gallery\" content-type=\"image/png\" url=\"javascript:alert(1)\"></action-text-attachment></div>",
        "<base href=\"https://evil.test/\"><meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\"><link rel=\"import\" href=\"x\">",
        "<a href=\"https://example.com/\" target=\"_blank\" rel=\"opener\" ping=\"https://evil.test/\">x</a>",
        "<b><i><u><s><table><td><img src=x onerror=alert(1)></b>",
        "<xmp><img src=x onerror=alert(1)></xmp><plaintext><img src=x onerror=alert(1)>",
    ];

    public static TheoryData<string> MalformedSignedGlobalIds =>
    [
        "", "--", "----x", "x--", "!!!--abc", "e30=--abc", "WzFd--abc", "bnVsbA==--x", "InN0ciI=--x",
        SignedGlobalId( "{\"_rails\":1}" ),
        SignedGlobalId( "{\"_rails\":{\"data\":1}}" ),
        SignedGlobalId( "{\"_rails\":{\"data\":\"gid://campfire/Admin/1\"}}" ),
        SignedGlobalId( "{\"_rails\":{\"data\":\"gid://campfire/User/999\"}}" ),
        SignedGlobalId( "{\"_rails\":{\"message\":\"!!\"}}" ),
        SignedGlobalId( "{\"_rails\":{\"message\":1}}" ),
        SignedGlobalId( new string( '[', 5_000 ) ),
        SignedGlobalId( "\"\\ud800\"" ),
        Convert.ToBase64String( [0xff, 0xfe, 0x7b] ) + "--x",
        new string( 'A', 100_000 ) + "--x",
    ];

    [Theory]
    [MemberData( nameof( CaseNames ) )]
    public void Presentation_passes_the_security_assertions( string name )
    {
        var testCase = RichTextCorpus.Case( name );

        var presentation = RichTextPipeline.PresentMessage( RichTextCorpus.Body( testCase ), RichTextCorpus.ContextFor( testCase ) );

        Assert.Empty( SecurityAssertions.PresentationViolations( presentation.Html ) );
    }

    /// <summary>Rails' own presentation, as the documented security fixes render it.</summary>
    [Theory]
    [MemberData( nameof( CaseNames ) )]
    public void Rails_presentation_with_the_security_fixes_passes_the_security_assertions( string name )
    {
        var (raised, value) = RichTextCorpus.Outcome( RichTextCorpus.Case( name )["presentation"] );
        var rails = raised ? "" : value?.GetValue<string>( ) ?? "";

        Assert.Empty( SecurityAssertions.PresentationViolations( SecurityFixExpectations.Presentation( name, rails ) ) );
    }

    [Theory]
    [MemberData( nameof( CaseNames ) )]
    public void Rendered_html_passes_the_security_assertions( string name )
    {
        var testCase = RichTextCorpus.Case( name );
        string html;
        try
        {
            html = RichTextPipeline.RenderHtml( RichTextCorpus.Body( testCase ), RichTextCorpus.ContextFor( testCase ) );
        }
        catch ( RichTextException )
        {
            // Rails raises too (the JSON view fails); nothing is rendered
            return;
        }

        Assert.Empty( SecurityAssertions.RenderedHtmlViolations( html ) );
    }

    [Theory]
    [MemberData( nameof( AdversarialBodies ) )]
    public void Adversarial_bodies_are_neutralized( string body )
    {
        Assert.Empty( SecurityAssertions.PresentationViolations( RichTextPipeline.PresentMessage( body, ReferenceFixtures.Context ).Html ) );
        Assert.Empty( SecurityAssertions.RenderedHtmlViolations( RenderedOrEmpty( body ) ) );
    }

    [Theory]
    [MemberData( nameof( MalformedSignedGlobalIds ) )]
    public void Malformed_sgids_never_mention_anyone_or_crash( string signedGlobalId )
    {
        var body = $"<p>hi <action-text-attachment sgid=\"{signedGlobalId}\" content-type=\"application/vnd.campfire.mention\"></action-text-attachment></p>";

        var presentation = RichTextPipeline.PresentMessage( body, ReferenceFixtures.Context );

        Assert.DoesNotContain( "mention", presentation.Html, StringComparison.Ordinal );
        Assert.Empty( SecurityAssertions.PresentationViolations( presentation.Html ) );
        Assert.Empty( RichTextPipeline.MentionedUsers( body, ReferenceFixtures.Context ) );
        RenderedOrEmpty( body );
    }

    /// <summary>The assertions have to catch what they are for, or passing them means nothing.</summary>
    [Theory]
    [InlineData( "<script>alert(1)</script>" )]
    [InlineData( "<p onclick=\"x()\">p</p>" )]
    [InlineData( "<a href=\"java\tscript:alert(1)\">x</a>" )]
    [InlineData( "<a href=\" JAVASCRIPT:alert(1)\">x</a>" )]
    [InlineData( "<img src=\"data:text/html,<script>\">" )]
    [InlineData( "<span style=\"color: red\">x</span>" )]
    [InlineData( "<svg><a xlink:href=\"javascript:1\">x</a></svg>" )]
    [InlineData( "<span data-controller=\"x\">x</span>" )]
    [InlineData( "<p title=\"a\" _blank\"=\"\">x</p>" )]
    [InlineData( "<details>x</details>" )]
    [InlineData( "<img name=\"body\" src=\"/x\">" )]
    [InlineData( "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript>" )]
    public void Security_assertions_catch_planted_defects( string html ) =>
        Assert.NotEmpty( SecurityAssertions.PresentationViolations( html ) );

    [Fact]
    public void Security_assertions_pass_benign_markup( )
    {
        Assert.Empty( SecurityAssertions.PresentationViolations( "<p><a target=\"_blank\" href=\"https://example.com\">x</a><img src=\"/a.png\" alt=\"a\">" +
            "</p><pre data-language=\"ruby\">x</pre>" ) );
        Assert.Empty( SecurityAssertions.RenderedHtmlViolations( "<div class=\"lexxy-content\"><mark style=\"color: var(--highlight-1);\">x</mark></div>" ) );
        Assert.NotEmpty( SecurityAssertions.RenderedHtmlViolations( "<span style=\"position: fixed\">x</span>" ) );
    }

    private static string RenderedOrEmpty( string body )
    {
        try
        {
            return RichTextPipeline.RenderHtml( body, ReferenceFixtures.Context );
        }
        catch ( RichTextException )
        {
            return "";
        }
    }

    private static string SignedGlobalId( string json ) => Convert.ToBase64String( Encoding.UTF8.GetBytes( json ) ) + "--x";
}
