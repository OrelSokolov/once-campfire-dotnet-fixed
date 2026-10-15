namespace Campfire.Web.Views.Rendering;

/// <summary>
/// The <c>content_for</c> regions the application layout yields (<c>yield :head</c>, ...), which pages fill by
/// overriding <c>ExecuteSectionAsync</c>.
/// </summary>
public static class ContentFor
{
    public const string Head = "head";
    public const string Nav = "nav";
    public const string Sidebar = "sidebar";
    public const string Footer = "footer";
}
