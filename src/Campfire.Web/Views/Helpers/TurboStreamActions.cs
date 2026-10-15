namespace Campfire.Web.Views.Helpers;

/// <summary>The <c>action</c> of a <c>&lt;turbo-stream&gt;</c> element (<c>turbo_stream.&lt;action&gt;</c>).</summary>
public static class TurboStreamActions
{
    public const string Append = "append";
    public const string Prepend = "prepend";
    public const string Replace = "replace";
    public const string Update = "update";
    public const string Before = "before";
    public const string After = "after";

    /// <summary>Removes the target; the element carries no template.</summary>
    public const string Remove = "remove";

    /// <summary>Reloads the page; the element carries no template.</summary>
    public const string Refresh = "refresh";
}
