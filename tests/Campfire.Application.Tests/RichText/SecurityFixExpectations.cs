using System.Text;

namespace Campfire.Application.Tests.RichText;

/// <summary>The security fixes of docs/compatibility.md#deliberate-differences-and-bounds that change corpus outputs.</summary>
[Flags]
internal enum SecurityFix
{
    None = 0,

    /// <summary>
    /// "Autolink attribute breakout (stored XSS)": rails_autolink scans Nokogiri's serialization,
    /// which leaves <c>&lt;</c> and <c>&gt;</c> raw in attribute values, so a URL after a <c>&gt;</c> in
    /// a <c>title</c> got a link inserted inside the attribute, closing it early. The port escapes the
    /// brackets in attribute values, and those URLs stay text.
    /// </summary>
    AutolinkAttributeBreakout = 1,

    /// <summary>"<c>name</c> DOM clobbering": <c>name</c> attributes are dropped from every allowlist.</summary>
    NameDomClobbering = 2,
}

/// <summary>
/// Expected presentations for the corpus cases a security fix changes. Every other case keeps the
/// output Rails generated. The expected output of an affected case is Rails' output with the fix
/// applied by <see cref="ApplyFixes"/>, a rule over Rails' markup independent of the pipeline, and
/// <see cref="AffectedCases"/> names each affected case with the security fixes that change it; the
/// corpus test checks that the rule changes exactly those cases.
/// </summary>
internal static class SecurityFixExpectations
{
    private const string InsertedLink = "<a target=\"_blank\" href=\"";

    private const string NameAttribute = " name=\"";

    /// <summary>The corpus cases whose presentation differs from Rails' because of a documented security fix.</summary>
    public static readonly IReadOnlyDictionary<string, SecurityFix> AffectedCases = new Dictionary<string, SecurityFix>( StringComparer.Ordinal )
    {
        // A user name with markup, rendered into the mention's title attribute
        ["lexxy mention html name"] = SecurityFix.AutolinkAttributeBreakout,

        // An image URL with brackets in the embed's src attribute
        ["og href with quotes and spaces"] = SecurityFix.AutolinkAttributeBreakout,

        // The stored XSS itself: title="x> http://..." with markup after the URL
        ["xss attribute with gt and url"] = SecurityFix.AutolinkAttributeBreakout,
        ["xss name attribute"] = SecurityFix.NameDomClobbering,

        // Generated bodies with the fuzzer's title="t > http://example.com/in-title" attribute
        ["fuzz 1"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 2"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 7"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 25"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 27"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 32"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 46"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 47"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 68"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 72"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 93"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 106"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 112"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 128"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 136"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 137"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 141"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 146"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 151"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 153"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 162"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 165"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 190"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 196"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 197"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 207"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 208"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 217"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 225"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 245"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 255"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 258"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 327"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 343"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 346"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 367"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 369"] = SecurityFix.AutolinkAttributeBreakout,
        ["fuzz 384"] = SecurityFix.AutolinkAttributeBreakout,
    };

    /// <summary>The expected presentation of a case: Rails' own, or Rails' with the case's documented security fixes applied.</summary>
    public static string Presentation( string name, string rails )
    {
        var fixes = AffectedCases.GetValueOrDefault( name );

        return fixes == SecurityFix.None ? rails : ApplyFixes( rails, fixes );
    }

    /// <summary>
    /// Rails' presentation as the fixes render it: in attribute values <c>&lt;</c> and <c>&gt;</c> are
    /// escaped and a link rails_autolink inserted is the text it replaced
    /// (<see cref="SecurityFix.AutolinkAttributeBreakout"/>); <c>name</c> attributes are gone
    /// (<see cref="SecurityFix.NameDomClobbering"/>).
    /// </summary>
    public static string ApplyFixes( string rails, SecurityFix fixes )
    {
        ArgumentNullException.ThrowIfNull( rails );

        var output = new StringBuilder( rails.Length );
        var state = MarkupState.Text;
        var position = 0;
        while ( position < rails.Length )
        {
            if ( state == MarkupState.Value && fixes.HasFlag( SecurityFix.AutolinkAttributeBreakout ) && rails.AsSpan( position ).StartsWith( InsertedLink ) )
            {
                position = AppendLinkText( rails, position, output );
                continue;
            }

            if ( state == MarkupState.Tag && fixes.HasFlag( SecurityFix.NameDomClobbering ) && rails.AsSpan( position ).StartsWith( NameAttribute ) )
            {
                position = rails.IndexOf( '"', position + NameAttribute.Length ) + 1;
                continue;
            }

            var character = rails[position++];
            state = Next( state, character );
            if ( state == MarkupState.Value && character is '<' or '>' && fixes.HasFlag( SecurityFix.AutolinkAttributeBreakout ) )
            {
                output.Append( character == '<' ? "&lt;" : "&gt;" );
            }
            else
            {
                output.Append( character );
            }
        }

        return output.ToString( );
    }

    /// <summary>The text of an inserted <c>&lt;a target="_blank" href="..."&gt;text&lt;/a&gt;</c>, in place of the link.</summary>
    private static int AppendLinkText( string rails, int start, StringBuilder output )
    {
        var textStart = rails.IndexOf( "\">", start, StringComparison.Ordinal ) + 2;
        var textEnd = rails.IndexOf( "</a>", textStart, StringComparison.Ordinal );
        output.Append( rails.AsSpan( textStart, textEnd - textStart ).ToString( ).Replace( ">", "&gt;", StringComparison.Ordinal ) );

        return textEnd + "</a>".Length;
    }

    private enum MarkupState
    {
        Text,
        Tag,
        Value,
    }

    private static MarkupState Next( MarkupState state, char character ) => (state, character) switch
    {
        (MarkupState.Text, '<' ) => MarkupState.Tag,
        (MarkupState.Tag, '>' ) => MarkupState.Text,
        (MarkupState.Tag, '"' ) => MarkupState.Value,
        (MarkupState.Value, '"' ) => MarkupState.Tag,
        _ => state,
    };
}
