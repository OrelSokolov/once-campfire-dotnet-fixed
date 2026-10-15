using Campfire.Application.RichText;
using Campfire.Application.RichText.Attachments;
using Campfire.Application.Tests.RichText.Support;

namespace Campfire.Application.Tests.RichText;

/// <summary>
/// The differential gate against the Rails pipeline: every corpus case's presentation, plain text,
/// editor value and mentioned users, and the opengraph URL checks, compared byte for byte with what
/// Rails produced. The only expected outputs that differ from Rails' are the security fixes'
/// (<see cref="SecurityFixExpectations"/>), documented in docs/compatibility.md#deliberate-differences-and-bounds.
/// </summary>
public sealed class RichTextCorpusTests
{
    public static TheoryData<string> CaseNames => [.. RichTextCorpus.Cases.Select( RichTextCorpus.Name )];

    public static TheoryData<string> WebUrlValues => [.. RichTextCorpus.WebUrls.Select( url => url["value"]!.GetValue<string>( ) )];

    [Fact]
    public void Corpus_has_every_case_and_url( )
    {
        Assert.Equal( 647, RichTextCorpus.Cases.Count );
        Assert.Equal( 54, RichTextCorpus.WebUrls.Count );
    }

    /// <summary>The labeled list of security-fix cases is exactly the cases the fixes change.</summary>
    [Fact]
    public void Security_fix_expectations_name_exactly_the_cases_the_fixes_change( )
    {
        var changed = new List<string>( );
        foreach ( var testCase in RichTextCorpus.Cases )
        {
            var (raised, value) = RichTextCorpus.Outcome( testCase["presentation"] );
            var rails = raised ? "" : value?.GetValue<string>( ) ?? "";
            var fixes = SecurityFix.None;
            foreach ( var fix in ( SecurityFix[] ) [SecurityFix.AutolinkAttributeBreakout, SecurityFix.NameDomClobbering] )
            {
                if ( SecurityFixExpectations.ApplyFixes( rails, fix ) != rails )
                {
                    fixes |= fix;
                }
            }

            if ( fixes != SecurityFix.None )
            {
                changed.Add( $"[\"{RichTextCorpus.Name( testCase )}\"] = {Describe( fixes )}," );
            }
        }

        var declared = SecurityFixExpectations.AffectedCases.Select( pair => $"[\"{pair.Key}\"] = {Describe( pair.Value )}," );
        Assert.Equal( changed.Order( StringComparer.Ordinal ), declared.Order( StringComparer.Ordinal ) );
    }

    private static string Describe( SecurityFix fixes ) =>
        string.Join(
            " | ",
            Enum.GetValues<SecurityFix>( ).Where( fix => fix != SecurityFix.None && fixes.HasFlag( fix ) ).Select( fix => $"SecurityFix.{fix}" ) );

    [Theory]
    [MemberData( nameof( CaseNames ) )]
    public void Presentation_matches_rails( string name )
    {
        var testCase = RichTextCorpus.Case( name );
        var (raised, value) = RichTextCorpus.Outcome( testCase["presentation"] );
        var expected = raised ? Unrenderable : SecurityFixExpectations.Presentation( name, value?.GetValue<string>( ) ?? "" );
        expected = NativeIdentifierExpectations.Presentation( name, expected );

        var presentation = RichTextPipeline.PresentMessage( RichTextCorpus.Body( testCase ), RichTextCorpus.ContextFor( testCase ) );
        var actual = presentation.IsUnrenderable ? Unrenderable : presentation.Html;

        Assert.Equal( expected, actual );
    }

    [Theory]
    [MemberData( nameof( CaseNames ) )]
    public void Plain_text_matches_rails( string name )
    {
        var testCase = RichTextCorpus.Case( name );
        var (raised, value) = RichTextCorpus.Outcome( testCase["plain_text"] );

        var actual = Capture( ( ) => RichTextPipeline.ToPlainText( RichTextCorpus.Body( testCase ), RichTextCorpus.ContextFor( testCase ) ) );

        var expected = NativeIdentifierExpectations.PlainText( name, raised ? Raised : value?.GetValue<string>( ) ?? "" );
        Assert.Equal( expected, actual );

        // Presentation must not mutate the loaded body used by a later plain-text operation.
        var reused = Capture( ( ) =>
        {
            var prepared = RichTextPipeline.PrepareMessage( RichTextCorpus.Body( testCase ), RichTextCorpus.ContextFor( testCase ) );
            var presentation = prepared.PresentMessage( );
            var (presentationRaised, presentationValue) = RichTextCorpus.Outcome( testCase["presentation"] );
            var expectedPresentation = presentationRaised
                ? Unrenderable
                : SecurityFixExpectations.Presentation( name, presentationValue?.GetValue<string>( ) ?? "" );
            expectedPresentation = NativeIdentifierExpectations.Presentation( name, expectedPresentation );
            Assert.Equal( expectedPresentation, presentation.IsUnrenderable ? Unrenderable : presentation.Html );
            return prepared.ToPlainText( );
        } );
        Assert.Equal( expected, reused );
    }

    [Theory]
    [MemberData( nameof( CaseNames ) )]
    public void Editable_value_matches_rails( string name )
    {
        var testCase = RichTextCorpus.Case( name );
        var (raised, value) = RichTextCorpus.Outcome( testCase["editable"] );
        var expected = raised ? Raised : value?.GetValue<string>( );

        var actual = Capture( ( ) => RichTextPipeline.EditableValue( RichTextCorpus.Body( testCase ), RichTextCorpus.ContextFor( testCase ) ) ?? NoValue );

        Assert.Equal( NativeIdentifierExpectations.Editable( name, expected ?? NoValue ), actual );
    }

    [Theory]
    [MemberData( nameof( CaseNames ) )]
    public void Mentioned_users_match_rails( string name )
    {
        var testCase = RichTextCorpus.Case( name );
        var (raised, value) = RichTextCorpus.Outcome( testCase["mentioned"] );
        var expected = raised ? Raised : string.Join( ",", value!.AsArray( ).Select( id => id!.GetValue<long>( ) ) );

        var actual = Capture( ( ) => string.Join(
            ",",
            RichTextPipeline.MentionedUsers( RichTextCorpus.Body( testCase ), RichTextCorpus.ContextFor( testCase ) ).Select( user => user.Id ) ) );

        Assert.Equal( expected, actual );
    }

    [Theory]
    [MemberData( nameof( WebUrlValues ) )]
    public void Opengraph_url_check_matches_rails( string value )
    {
        var url = RichTextCorpus.WebUrls.Single( item => item["value"]!.GetValue<string>( ) == value );
        var (raised, result) = RichTextCorpus.Outcome( url["result"] );

        var actual = Capture( ( ) => OpengraphEmbeds.WebUrl( value, url["host"]!.GetValue<string>( ) ) ?? NoValue );

        Assert.Equal( raised ? Raised : result?.GetValue<string>( ) ?? NoValue, actual );
    }

    private const string Unrenderable = "(messages/_unrenderable)";

    private const string Raised = "(raised)";

    private const string NoValue = "(nil)";

    private static string Capture( Func<string> render )
    {
        try
        {
            return render( );
        }
        catch ( RichTextException )
        {
            return Raised;
        }
    }
}
