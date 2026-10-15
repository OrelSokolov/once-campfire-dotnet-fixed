namespace Campfire.Application.Integrations.Opengraph;

/// <summary>The Opengraph attributes an unfurl keeps, and the <c>meta</c> attributes they are read from.</summary>
internal static class OpengraphAttributeNames
{
    // Opengraph::Metadata::ATTRIBUTES
    public const string Title = "title";
    public const string Url = "url";
    public const string Image = "image";
    public const string Description = "description";

    // <meta> attributes
    public const string Property = "property";
    public const string Name = "name";
    public const string Content = "content";
    public const string Charset = "charset";
    public const string HttpEquiv = "http-equiv";

    /// <summary>What a <c>property</c> or <c>name</c> starts with to be an Opengraph tag.</summary>
    public const string Prefix = "og:";
}
