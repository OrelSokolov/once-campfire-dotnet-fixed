using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Campfire.Application.RichText.Sanitization;

namespace Campfire.Application.Tests.RichText.Support;

/// <summary>
/// Security properties of rendered rich text that don't depend on the Rails oracle. Output is parsed
/// the way a current browser would (AngleSharp's full HTML parser, which follows the current
/// standard rather than Gumbo), so a parser difference that turns markup live is caught too.
/// </summary>
internal static class SecurityAssertions
{
    /// <summary>Elements that run script, load active content, submit, or reparse their content.</summary>
    private static readonly HashSet<string> DangerousElements = new( StringComparer.Ordinal )
    {
        "script", "style", "iframe", "frame", "frameset", "object", "embed", "applet", "base", "meta", "link", "form",
        "input", "button", "textarea", "select", "svg", "math", "template", "noscript", "xmp", "plaintext", "noembed",
    };

    private static readonly HashSet<string> UrlAttributes = new( StringComparer.Ordinal )
    {
        "href", "src", "action", "formaction", "poster", "cite", "background", "xlink:href", "srcset", "data",
    };

    private static readonly HtmlParser BrowserParser = new( new HtmlParserOptions { IsScripting = true } );

    /// <summary>
    /// What a message's presentation may contain: auto_link's allowlist, plus the <c>target</c> it adds and
    /// the layout's <c>div</c>; no <c>style</c>, no <c>data-</c> attribute but <c>data-language</c>.
    /// </summary>
    public static List<string> PresentationViolations( string html ) =>
        Violations(
            html,
            element => SafeList.AutoLink.AllowsTag( element ),
            attribute => SafeList.AutoLink.AllowsAttribute( attribute ) || attribute == "target",
            allowStyle: false );

    /// <summary>
    /// What <c>message.body.to_s</c> may contain: Action Text's allowlist (which includes media elements)
    /// and <c>style</c> limited to highlight colors.
    /// </summary>
    public static List<string> RenderedHtmlViolations( string html ) =>
        Violations(
            html,
            element => SafeList.ActionText.AllowsTag( element ) || element == "div",
            attribute => SafeList.ActionText.AllowsAttribute( attribute ) || attribute == "class",
            allowStyle: true );

    private static List<string> Violations( string html, Func<string, bool> allowsTag, Func<string, bool> allowsAttribute, bool allowStyle )
    {
        var violations = new List<string>( );
        using var document = BrowserParser.ParseDocument( "<!DOCTYPE html><body>" + html );
        foreach ( var element in document.Body!.Descendants<IElement>( ) )
        {
            CheckElement( element, allowsTag, violations );
            foreach ( var attribute in element.Attributes )
            {
                CheckAttribute( element, attribute, allowsAttribute, allowStyle, violations );
            }
        }

        return violations;
    }

    private static void CheckElement( IElement element, Func<string, bool> allowsTag, List<string> violations )
    {
        var name = element.LocalName;
        var allowedMedia = name is "video" or "audio" or "source" or "embed" && allowsTag( name );
        if ( DangerousElements.Contains( name ) && !allowedMedia )
        {
            violations.Add( $"<{name}> element" );
        }

        if ( element.NamespaceUri != NamespaceNames.HtmlUri )
        {
            violations.Add( $"<{name}> in {element.NamespaceUri}" );
        }

        if ( !allowsTag( name ) )
        {
            violations.Add( $"<{name}> not in the allowlist" );
        }
    }

    private static void CheckAttribute( IElement element, IAttr attribute, Func<string, bool> allowsAttribute, bool allowStyle, List<string> violations )
    {
        var name = attribute.Name.ToLowerInvariant( );
        var where = $"{attribute.Name}={attribute.Value} on <{element.LocalName}>";
        if ( name.StartsWith( "on", StringComparison.Ordinal ) )
        {
            violations.Add( $"event handler {where}" );
        }

        if ( UrlAttributes.Contains( name ) && IsDangerousUrl( attribute.Value ) )
        {
            violations.Add( $"executable URL {where}" );
        }

        if ( name == "style" && !(allowStyle && IsHighlightStyle( attribute.Value )) )
        {
            violations.Add( $"style {where}" );
        }

        if ( name is "name" or "id" || (name.StartsWith( "data-", StringComparison.Ordinal ) && name != "data-language") )
        {
            violations.Add( $"DOM clobbering or controller hook {where}" );
        }

        if ( !allowsAttribute( name ) && name != "style" )
        {
            violations.Add( $"{where} not in the allowlist" );
        }
    }

    /// <summary>What a browser would see: control characters and whitespace ignored, case folded.</summary>
    public static bool IsDangerousUrl( string value )
    {
        var cleaned = new string( [.. value.Where( character => !char.IsControl( character ) && !char.IsWhiteSpace( character ) )] ).ToLowerInvariant( );
        if ( cleaned.StartsWith( "javascript:", StringComparison.Ordinal )
            || cleaned.StartsWith( "vbscript:", StringComparison.Ordinal )
            || cleaned.StartsWith( "livescript:", StringComparison.Ordinal ) )
        {
            return true;
        }

        if ( !cleaned.StartsWith( "data:", StringComparison.Ordinal ) )
        {
            return false;
        }

        // A data: URL is only dangerous as markup or script; an unparseable media type is text/plain
        var mediaType = cleaned[5..].Split( ',' )[0].Split( ';' )[0];
        var slash = mediaType.IndexOf( '/', StringComparison.Ordinal );
        var valid = slash > 0
            && slash < mediaType.Length - 1
            && mediaType.All( character => char.IsAsciiLetterOrDigit( character ) || "!#$%&'*+-.^_`|~/".Contains( character, StringComparison.Ordinal ) );

        return valid && (( string[] ) ["html", "xml", "svg", "script", "ecmascript"]).Any( kind => mediaType.Contains( kind, StringComparison.Ordinal ) );
    }

    /// <summary>The style restriction: only color and background-color declarations with plain color values.</summary>
    private static bool IsHighlightStyle( string style ) =>
        style.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).All( IsHighlightDeclaration );

    private static bool IsHighlightDeclaration( string declaration )
    {
        var parts = declaration.Split( ':', 2, StringSplitOptions.TrimEntries );
        if ( parts.Length != 2 || parts[0].ToLowerInvariant( ) is not ("color" or "background-color") )
        {
            return false;
        }

        var value = parts[1].ToLowerInvariant( );
        var isFunction = value.Contains( '(', StringComparison.Ordinal );
        var isColorFunction = value.StartsWith(
            "var(",
            StringComparison.Ordinal ) || value.StartsWith( "rgb", StringComparison.Ordinal ) || value.StartsWith( "hsl", StringComparison.Ordinal );

        return (!isFunction || isColorFunction)
            && !value.Contains( "url", StringComparison.Ordinal ) && !value.Contains( "expression", StringComparison.Ordinal )
            && !value.Contains( '\\', StringComparison.Ordinal ) && !value.Contains( "/*", StringComparison.Ordinal );
    }
}
