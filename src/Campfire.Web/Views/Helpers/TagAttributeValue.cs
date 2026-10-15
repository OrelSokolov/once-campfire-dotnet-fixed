using System.Globalization;

namespace Campfire.Web.Views.Helpers;

/// <summary>An option value as Rails' <c>tag_options</c> sees it: plain text, an html_safe string, or a boolean.</summary>
public sealed class TagAttributeValue
{
    private TagAttributeValue( TagAttributeKind kind, string text, bool flag )
    {
        Kind = kind;
        Text = text;
        Flag = flag;
    }

    public TagAttributeKind Kind { get; }

    /// <summary>The text of a <see cref="TagAttributeKind.Text"/> or <see cref="TagAttributeKind.Safe"/> value, or "true"/"false".</summary>
    public string Text { get; }

    public bool Flag { get; }

    public static TagAttributeValue Of( string text ) => new( TagAttributeKind.Text, text, false );

    public static TagAttributeValue Of( SafeHtml html ) => new( TagAttributeKind.Safe, html.Html, false );

    public static TagAttributeValue Of( bool flag ) => new( TagAttributeKind.Bool, flag ? "true" : "false", flag );

    public static TagAttributeValue Of( long number ) => new( TagAttributeKind.Text, number.ToString( CultureInfo.InvariantCulture ), false );

    public static implicit operator TagAttributeValue?( string? text ) => text is null ? null : Of( text );

    public static implicit operator TagAttributeValue?( SafeHtml? html ) => html is null ? null : Of( html );

    public static implicit operator TagAttributeValue( bool flag ) => Of( flag );

    public static implicit operator TagAttributeValue( long number ) => Of( number );

    public static implicit operator TagAttributeValue( int number ) => Of( number );

    public static TagAttributeValue? FromString( string? text ) => text;

    public static TagAttributeValue FromBoolean( bool flag ) => flag;

    public static TagAttributeValue FromInt64( long number ) => number;

    public static TagAttributeValue FromInt32( int number ) => number;

    public static TagAttributeValue? FromSafeHtml( SafeHtml? html ) => html;
}

/// <summary>How a <see cref="TagAttributeValue"/> renders.</summary>
public enum TagAttributeKind
{
    /// <summary>Escaped on output.</summary>
    Text,

    /// <summary>html_safe: only <c>"</c> is replaced on output.</summary>
    Safe,

    /// <summary>A boolean attribute (<c>name="name"</c> or omitted) or the text "true"/"false".</summary>
    Bool,
}
