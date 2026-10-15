using System.Text;
using System.Text.RegularExpressions;
using Campfire.Application.RichText.Html;

namespace Campfire.Application.RichText.Sanitization;

/// <summary>
/// <c>Rails::HTML5::SafeListSanitizer</c> with a <c>Rails::HTML::PermitScrubber</c>, over Loofah's
/// HTML5 scrubbing helpers (rails-html-sanitizer 1.7.1, loofah 2.25.2). Every sanitization layer in
/// the pipeline is this one scrubber with a different <see cref="SafeList"/>.
/// </summary>
internal static partial class SafeListSanitizer
{
    /// <summary>Lexxy's <c>ALLOWED_STYLE_PROPERTIES</c>.</summary>
    private static readonly string[] AllowedStyleProperties = ["color", "background-color"];

    private enum Outcome
    {
        Keep,
        Unwrap,
        Drop,
    }

    /// <summary><c>SafeListSanitizer#sanitize(html, tags:, attributes:)</c>.</summary>
    public static string Sanitize( string html, SafeList list, AttributeEscaping escaping = AttributeEscaping.Nokogiri )
    {
        ArgumentNullException.ThrowIfNull( html );
        ArgumentNullException.ThrowIfNull( list );

        if ( html.Length == 0 )
        {
            return "";
        }

        var fragment = HtmlFragmentParser.ParseFragment( html );
        ScrubChildren( fragment, list );

        return HtmlSerializer.ToHtml( fragment, escaping );
    }

    /// <summary>
    /// <c>Loofah::Scrubber#traverse_conditionally_bottom_up</c>: each child's subtree (as the children
    /// were before any of them was scrubbed) first, then the child itself. The parent's child list is
    /// rebuilt in one pass, so unwrapping or dropping many siblings stays linear. The recursion is as
    /// deep as the parsed tree, which the parser bounds.
    /// </summary>
    private static void ScrubChildren( HtmlNode node, SafeList list )
    {
        var scrubbed = new List<HtmlNode>( node.Children.Count );
        foreach ( var child in node.Children.ToList( ) )
        {
            if ( child.Children.Count > 0 )
            {
                ScrubChildren( child, list );
            }

            switch ( Scrub( child, list ) )
            {
                case Outcome.Keep:
                    scrubbed.Add( child );
                    break;
                case Outcome.Unwrap:
                    scrubbed.AddRange( child.DetachChildren( ) );
                    break;
            }
        }

        node.ReplaceChildren( scrubbed );
    }

    /// <summary>
    /// <c>Rails::HTML::PermitScrubber#scrub</c>. Disallowed HTML elements are unwrapped (and comments,
    /// which have no children, removed); disallowed foreign (SVG, MathML) elements are removed together
    /// with their contents, since they carry a namespace.
    /// </summary>
    private static Outcome Scrub( HtmlNode node, SafeList list )
    {
        if ( node.IsText )
        {
            return Outcome.Keep;
        }

        if ( node.Kind == HtmlNodeKind.CData )
        {
            // Loofah's cdata_escape: a CDATA section is kept with its tags escaped. The parser only makes
            // them inside SVG and MathML, which are removed with their contents anyway.
            node.Text = node.Text
                .Replace( "&", "&amp;", StringComparison.Ordinal )
                .Replace( "<", "&lt;", StringComparison.Ordinal )
                .Replace( ">", "&gt;", StringComparison.Ordinal );
            return Outcome.Keep;
        }

        if ( !node.IsElement || !list.AllowsTag( node.LocalName ) )
        {
            return node is { IsElement: true, IsHtmlElement: false } ? Outcome.Drop : Outcome.Unwrap;
        }

        ScrubAttributes( node, list );

        return Outcome.Keep;
    }

    /// <summary>
    /// <c>PermitScrubber#scrub_attributes</c> with an attribute allowlist. Attributes are visited in
    /// order, and each allowed one re-escapes every URL attribute on the node as it goes, so a later URL
    /// is checked in its re-escaped form (" javascript:" has become "%20javascript:" and passes).
    /// </summary>
    private static void ScrubAttributes( HtmlNode node, SafeList list )
    {
        foreach ( var name in node.Attributes.Select( attribute => attribute.QualifiedName ).ToList( ) )
        {
            if ( node.GetAttribute( name ) is not { } value )
            {
                continue;
            }

            if ( !list.AllowsAttribute( name ) || (LoofahUri.AttributesWithUriValues.Contains( name ) && !LoofahUri.IsAllowed( value )) )
            {
                node.RemoveAttribute( name );
                continue;
            }

            if ( name == "src" && value.All( char.IsWhiteSpace ) )
            {
                node.RemoveAttribute( name );
            }

            ForceCorrectAttributeEscaping( node );
        }

        ScrubStyle( node );
    }

    /// <summary>
    /// <c>Loofah::HTML5::Scrub.force_correct_attribute_escaping!</c> (libxml2 builds only, which CRuby
    /// is): spaces and double quotes in <c>href</c>, <c>action</c>, <c>src</c> and an <c>a</c>'s
    /// <c>name</c> become <c>%20</c> and <c>%22</c>. The value is written back through
    /// <c>Nokogiri::XML::Attr#value=</c>, where libxml2 drops the C0 controls XML 1.0 doesn't allow.
    /// </summary>
    private static void ForceCorrectAttributeEscaping( HtmlNode node )
    {
        var isAnchor = node.LocalName == "a";
        foreach ( var attribute in node.Attributes )
        {
            var qualifies = attribute.QualifiedName is "href" or "action" or "src" || (attribute.QualifiedName == "name" && isAnchor);
            if ( !qualifies )
            {
                continue;
            }

            var escaped = new StringBuilder( attribute.Value.Length );
            foreach ( var character in attribute.Value )
            {
                _ = character switch
                {
                    ' ' => escaped.Append( "%20" ),
                    '"' => escaped.Append( "%22" ),
                    '\t' or '\n' or '\r' => escaped.Append( character ),
                    < ' ' => escaped,
                    _ => escaped.Append( character ),
                };
            }

            attribute.Value = escaped.ToString( );
        }
    }

    /// <summary>
    /// Where Loofah's <c>scrub_css_attribute</c> runs <c>style</c> through its CSS scrubber, this keeps
    /// only what Lexxy writes (security fix: "<c>style</c> scrubbing", decided fix): highlight colors
    /// (<c>color</c> and <c>background-color</c>, which Lexxy's own paste filter also limits
    /// <c>style</c> to) with plain color values. A message's presentation drops <c>style</c> in
    /// auto_link anyway, but the HTML body bots and webhooks get keeps it.
    /// </summary>
    private static void ScrubStyle( HtmlNode node )
    {
        if ( node.GetAttribute( "style" ) is not { } style )
        {
            return;
        }

        var declarations = style.Split( ';' )
            .Where( declaration => declaration.Trim( ).Length > 0 )
            .Select( ParseDeclaration )
            .ToList( );
        if ( declarations.Count > 0 && declarations.All( IsAllowedDeclaration ) )
        {
            return;
        }

        var scrubbed = string.Concat( declarations.Where( IsAllowedDeclaration ).Select( declaration => $"{declaration.Property}: {declaration.Value};" ) );
        if ( scrubbed.Length == 0 )
        {
            node.RemoveAttribute( "style" );
        }
        else
        {
            node.SetAttribute( "style", scrubbed );
        }
    }

    private static (string Property, string Value) ParseDeclaration( string declaration )
    {
        var colonIndex = declaration.IndexOf( ':', StringComparison.Ordinal );
        var property = colonIndex < 0 ? declaration : declaration[..colonIndex];
        var value = colonIndex < 0 ? "" : declaration[(colonIndex + 1)..];

        return (AsciiDowncase( property.Trim( ) ), value.Trim( ));
    }

    private static string AsciiDowncase( string value ) =>
        string.Create( value.Length, value, static ( span, source ) =>
        {
            for ( var index = 0; index < source.Length; index++ )
            {
                span[index] = char.IsAsciiLetterUpper( source[index] ) ? ( char ) (source[index] + 32) : source[index];
            }
        } );

    private static bool IsAllowedDeclaration( (string Property, string Value) declaration ) =>
        AllowedStyleProperties.Contains( declaration.Property ) && Ascii.IsValid( declaration.Value ) && PlainColor( ).IsMatch( declaration.Value );

    /// <summary>
    /// A color keyword, a hex color, a custom property (<c>var(--highlight-1)</c>, as Lexxy's
    /// highlights are), or an <c>rgb()</c>/<c>hsl()</c> color: nothing that can load a URL, escape, or
    /// run an expression. (ASCII only, which the caller checks.)
    /// </summary>
    [GeneratedRegex(
        @"\A(?:[a-z]+|#[0-9a-f]{3,8}|var\(\s*--[a-z0-9_-]+\s*\)|(?:rgb|rgba|hsl|hsla)\([0-9a-z.,%\s/+-]*\))\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )]
    private static partial Regex PlainColor( );
}
