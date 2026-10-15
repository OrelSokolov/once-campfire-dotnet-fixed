namespace Campfire.Web.Http;

/// <summary>What a piece was compressed after: nothing, a text part, a fragment, or a fragment run (from its start, or cut by the window).</summary>
internal enum GzipHistoryKind
{
    Empty,
    Text,
    Fragment,
    RunStart,
    RunCut,
}
