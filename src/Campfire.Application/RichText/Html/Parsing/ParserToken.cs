using System.Text;

namespace Campfire.Application.RichText.Html.Parsing;

/// <summary>Gumbo's token types; character data is split into runs of one type.</summary>
internal enum ParserTokenKind
{
    Doctype,
    StartTag,
    EndTag,
    Comment,

    /// <summary>A run of <c>\t \n \f \r</c> and space (Gumbo's <c>GUMBO_TOKEN_WHITESPACE</c>).</summary>
    Whitespace,

    /// <summary>A run of other characters.</summary>
    Character,

    /// <summary>A run of U+0000 characters read in the data state.</summary>
    Null,

    /// <summary>The content of a <c>&lt;![CDATA[...]]&gt;</c> section in foreign content.</summary>
    CData,

    EndOfFile,
}

/// <summary>A token as the tree builder sees it.</summary>
internal sealed class ParserToken( ParserTokenKind kind, string name = "", string text = "" )
{
    public ParserTokenKind Kind { get; } = kind;

    /// <summary>The lowercased tag name of a start or end tag; rewritten for <c>&lt;image&gt;</c>.</summary>
    public string Name { get; set; } = name;

    /// <summary>The characters of a character run or the data of a comment.</summary>
    public string Text { get; set; } = text;

    /// <summary>The tag's attributes, first occurrence of each name only, in source order.</summary>
    public List<HtmlAttribute> Attributes { get; init; } = [];

    public bool IsSelfClosing { get; init; }

    public bool IsCharacterData => Kind is ParserTokenKind.Whitespace or ParserTokenKind.Character or ParserTokenKind.Null or ParserTokenKind.CData;

    public static ParserToken EndOfFile { get; } = new( ParserTokenKind.EndOfFile );

    public bool IsStartTag( string tagName ) => Kind == ParserTokenKind.StartTag && Name == tagName;

    public bool IsEndTag( string tagName ) => Kind == ParserTokenKind.EndTag && Name == tagName;

    public bool IsStartTagIn( IReadOnlySet<string> names ) => Kind == ParserTokenKind.StartTag && names.Contains( Name );

    public bool IsEndTagIn( IReadOnlySet<string> names ) => Kind == ParserTokenKind.EndTag && names.Contains( Name );

    public bool HasAttribute( string attributeName ) => HtmlAttribute.IndexOf( Attributes, attributeName ) >= 0;

    /// <summary>Gumbo's <c>attribute_matches</c>: the attribute's value equals <paramref name="value"/> ignoring ASCII case.</summary>
    public bool AttributeMatches( string attributeName, string value )
    {
        var index = HtmlAttribute.IndexOf( Attributes, attributeName );
        return index >= 0 && Ascii.EqualsIgnoreCase( Attributes[index].Value, value );
    }
}
