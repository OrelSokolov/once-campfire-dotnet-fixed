using System.Collections.Frozen;

namespace Campfire.Application.RichText.Html.Parsing;

/// <summary>
/// The element sets Gumbo's tree builder (Nokogiri 1.19.4, gumbo-parser/src/parser.c) consults.
/// They follow Gumbo where it departs from the HTML standard: its "special" set lacks
/// <c>main</c>, <c>search</c>, <c>source</c>, <c>track</c> and <c>keygen</c>, and its scope sets
/// include MathML <c>annotation-xml</c>.
/// </summary>
internal static class GumboTagSets
{
    /// <summary><c>is_special_node</c>'s HTML elements.</summary>
    public static readonly FrozenSet<string> SpecialHtml = Set(
        "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote", "body", "br", "button",
        "caption", "center", "col", "colgroup", "dd", "details", "dir", "div", "dl", "dt", "embed", "fieldset", "figcaption",
        "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup", "hr",
        "html", "iframe", "img", "input", "li", "link", "listing", "marquee", "menu", "meta", "nav", "noembed", "noframes",
        "noscript", "object", "ol", "p", "param", "plaintext", "pre", "script", "section", "select", "style", "summary", "table",
        "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "title", "tr", "ul", "wbr", "xmp" );

    /// <summary>The HTML part of <c>DEFAULT_SCOPE_TAGS</c>.</summary>
    public static readonly FrozenSet<string> DefaultScopeHtml = Set( "applet", "caption", "html", "table", "td", "th", "marquee", "object", "template" );

    public static readonly FrozenSet<string> MathMlTextIntegrationPoints = Set( "mi", "mo", "mn", "ms", "mtext" );

    public static readonly FrozenSet<string> SvgHtmlIntegrationPoints = Set( "foreignObject", "desc", "title" );

    public static readonly FrozenSet<string> ImpliedEndTags = Set( "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc" );

    public static readonly FrozenSet<string> ImpliedEndTagsThoroughly = Set(
        "caption", "colgroup", "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc", "tbody", "td", "tfoot", "th",
        "thead", "tr" );

    public static readonly FrozenSet<string> Headings = Set( "h1", "h2", "h3", "h4", "h5", "h6" );

    public static readonly FrozenSet<string> TableCells = Set( "td", "th" );

    public static readonly FrozenSet<string> DefinitionListItems = Set( "dd", "dt" );

    /// <summary>The start tags "in foreign content" breaks out of SVG and MathML for.</summary>
    public static readonly FrozenSet<string> ForeignBreakout = Set(
        "b", "big", "blockquote", "body", "br", "center", "code", "dd", "div", "dl", "dt", "em", "embed", "h1", "h2", "h3", "h4",
        "h5", "h6", "head", "hr", "i", "img", "li", "listing", "menu", "meta", "nobr", "ol", "p", "pre", "ruby", "s", "small",
        "span", "strong", "strike", "sub", "sup", "table", "tt", "u", "ul", "var" );

    /// <summary>The foster-parenting targets of <c>get_appropriate_insertion_location</c>.</summary>
    public static readonly FrozenSet<string> TableFosterTargets = Set( "table", "tbody", "tfoot", "thead", "tr" );

    /// <summary><c>gumbo_get_svg_tag_replacement</c>: SVG element names whose case the tree builder restores.</summary>
    public static readonly FrozenDictionary<string, string> SvgTagNames = CaseMap(
        "altGlyph", "altGlyphDef", "altGlyphItem", "animateColor", "animateMotion", "animateTransform", "clipPath", "feBlend",
        "feColorMatrix", "feComponentTransfer", "feComposite", "feConvolveMatrix", "feDiffuseLighting", "feDisplacementMap",
        "feDistantLight", "feFlood", "feFuncA", "feFuncB", "feFuncG", "feFuncR", "feGaussianBlur", "feImage",
        "feMerge", "feMergeNode", "feMorphology", "feOffset", "fePointLight", "feSpecularLighting", "feSpotLight", "feTile",
        "feTurbulence", "foreignObject", "glyphRef", "linearGradient", "radialGradient", "textPath" );

    /// <summary><c>gumbo_get_svg_attr_replacement</c>: SVG attribute names whose case the tree builder restores.</summary>
    public static readonly FrozenDictionary<string, string> SvgAttributeNames = CaseMap(
        "attributeName", "attributeType", "baseFrequency", "baseProfile", "calcMode", "clipPathUnits", "diffuseConstant",
        "edgeMode", "filterUnits", "glyphRef", "gradientTransform", "gradientUnits", "kernelMatrix", "kernelUnitLength",
        "keyPoints", "keySplines", "keyTimes", "lengthAdjust", "limitingConeAngle", "markerHeight", "markerUnits",
        "markerWidth", "maskContentUnits", "maskUnits", "numOctaves", "pathLength", "patternContentUnits", "patternTransform",
        "patternUnits", "pointsAtX", "pointsAtY", "pointsAtZ", "preserveAlpha", "preserveAspectRatio", "primitiveUnits", "refX",
        "refY", "repeatCount", "repeatDur", "requiredExtensions", "requiredFeatures", "specularConstant", "specularExponent",
        "spreadMethod", "startOffset", "stdDeviation", "stitchTiles", "surfaceScale", "systemLanguage", "tableValues", "targetX",
        "targetY", "textLength", "viewBox", "viewTarget", "xChannelSelector", "yChannelSelector", "zoomAndPan" );

    /// <summary><c>gumbo_get_foreign_attr_replacement</c>: (prefix, local name) of the namespaced foreign attributes.</summary>
    public static readonly FrozenDictionary<string, (string? Prefix, string LocalName)> ForeignAttributes = new Dictionary<string, (string?, string)>
    {
        ["xlink:actuate"] = ("xlink", "actuate"),
        ["xlink:arcrole"] = ("xlink", "arcrole"),
        ["xlink:href"] = ("xlink", "href"),
        ["xlink:role"] = ("xlink", "role"),
        ["xlink:show"] = ("xlink", "show"),
        ["xlink:title"] = ("xlink", "title"),
        ["xlink:type"] = ("xlink", "type"),
        ["xml:lang"] = ("xml", "lang"),
        ["xml:space"] = ("xml", "space"),
        ["xmlns"] = (null, "xmlns"),
        ["xmlns:xlink"] = ("xmlns", "xlink"),
    }.ToFrozenDictionary( StringComparer.Ordinal );

    public static FrozenSet<string> Set( params string[] names ) => names.ToFrozenSet( StringComparer.Ordinal );

    private static FrozenDictionary<string, string> CaseMap( params string[] names ) =>
        names.ToFrozenDictionary( name => name.ToLowerInvariant( ), name => name, StringComparer.Ordinal );
}
