namespace Campfire.Application.Serialization;

/// <summary>Whether encoded JSON escapes the characters that are unsafe inside HTML.</summary>
public enum JsonEscaping
{
    /// <summary>
    /// <c>ActiveSupport::JSON.encode</c> (<c>to_json</c>): <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, U+2028 and U+2029
    /// become <c>\u</c> escapes, so the JSON can sit in a page.
    /// </summary>
    HtmlEntities,

    /// <summary><c>JSON.generate</c>: only what JSON itself requires is escaped.</summary>
    None
}
