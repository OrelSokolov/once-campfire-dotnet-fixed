namespace Campfire.Web.Http;

/// <summary>
/// A registered MIME type (<c>Mime[:html]</c> and friends). Instances are singletons, so reference
/// equality is Rails' symbol equality.
/// </summary>
public sealed class MimeType
{
    internal MimeType( string symbol, string value, string[] synonyms, string[] extensions )
    {
        Symbol = symbol;
        Value = value;
        Synonyms = synonyms;
        Extensions = extensions;
    }

    /// <summary>The Rails symbol (<c>html</c>, <c>turbo_stream</c>); <c>*/*</c> for <see cref="Mime.All"/>.</summary>
    public string Symbol { get; }

    /// <summary>The type string, e.g. <c>text/html</c>.</summary>
    public string Value { get; }

    public IReadOnlyList<string> Synonyms { get; }

    public IReadOnlyList<string> Extensions { get; }

    public override string ToString( ) => Value;

    /// <summary><c>Mime::Type#match?</c>: <paramref name="pattern"/> appears in the type or one of its synonyms.</summary>
    internal bool Matches( string pattern ) =>
        Value.Contains( pattern, StringComparison.Ordinal ) || Synonyms.Any( synonym => synonym.Contains( pattern, StringComparison.Ordinal ) );
}
