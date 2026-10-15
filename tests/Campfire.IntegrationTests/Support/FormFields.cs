using System.Net;
using System.Text.RegularExpressions;

namespace Campfire.IntegrationTests.Support;

/// <summary>A page's form as a browser submits it: its hidden fields (<c>_method</c>, the per-form <c>authenticity_token</c>).</summary>
public static partial class FormFields
{
    /// <summary>
    /// The hidden fields of the first form posting to <paramref name="action"/> that has a field named
    /// <paramref name="fieldName"/>.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Of( string html, string action, string fieldName )
    {
        ArgumentNullException.ThrowIfNull( html );

        foreach ( Match form in FormPattern( ).Matches( html ) )
        {
            var body = form.Value;
            var postsTheField = body.Contains( $"action=\"{action}\"", StringComparison.Ordinal )
                && body.Contains( $"name=\"{fieldName}\"", StringComparison.Ordinal );
            if ( !postsTheField )
            {
                continue;
            }

            return [.. HiddenPattern( ).Matches( body ).Select( HiddenField )];
        }

        throw new InvalidOperationException( $"The page has no form posting to {action} with {fieldName}." );
    }

    private static (string Name, string Value) HiddenField( Match hidden ) =>
        (WebUtility.HtmlDecode( hidden.Groups[1].Value ), WebUtility.HtmlDecode( hidden.Groups[2].Value ));

    [GeneratedRegex( "<form\\b.*?</form>", RegexOptions.Singleline )]
    private static partial Regex FormPattern( );

    [GeneratedRegex( "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"" )]
    private static partial Regex HiddenPattern( );
}
