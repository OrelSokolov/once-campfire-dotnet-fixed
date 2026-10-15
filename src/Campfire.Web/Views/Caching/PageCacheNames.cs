using Campfire.Web.Http;

namespace Campfire.Web.Views.Caching;

/// <summary>The page cache's route labels and the event and reason names its counters report.</summary>
public static class PageCacheNames
{
    /// <summary>The cached pages.</summary>
    public static class PageRoutes
    {
        public const string Room = "room";
        public const string Messages = "messages";
        public const string Sidebar = "sidebar";
        public const string Search = "search";

        /// <summary>Every route at once, as a whole-cache clear reports.</summary>
        public const string All = "all";
    }

    /// <summary>What happened to a request or a stored page.</summary>
    public static class Events
    {
        public const string Lookup = "lookup";
        public const string Hit = "hit";
        public const string EarlyHit = "early_hit";
        public const string Miss = "miss";
        public const string Render = "render";
        public const string DuplicateRender = "duplicate_render";
        public const string CoalescedWaiter = "coalesced_waiter";
        public const string Store = "store";
        public const string StoreRejected = "store_rejected";
        public const string Eviction = "eviction";
        public const string Clear = "clear";
    }

    /// <summary>The counters' metric tag names.</summary>
    public static class Tags
    {
        public const string Route = "route";
        public const string Event = "event";
        public const string Reason = "reason";
    }

    /// <summary>Why an event happened.</summary>
    public static class Reasons
    {
        public const string Disabled = "disabled";
        public const string NotAdmittedPrefix = "not_admitted:";
        public const string ObserverFailure = NotAdmittedPrefix + "observer_failure";
        public const string KeyTooLong = "key_too_long";
        public const string Oversize = "oversize";
        public const string SupersededGeneration = "superseded_generation";
        public const string Absent = "absent";
        public const string Expired = "expired";
        public const string Budget = "budget";
        public const string WaiterBound = "waiter_bound";
        public const string WaitTimeout = "wait_timeout";
        public const string LeaderUnavailable = "leader_unavailable";
    }

    /// <summary>Why a request or its response isn't admitted (reported after <see cref="Reasons.NotAdmittedPrefix"/>).</summary>
    public static class Rejections
    {
        public const string Method = "method";
        public const string Session = "session";
        public const string SessionChanged = "session_changed";
        public const string Bot = "bot";
        public const string Flash = "flash";
        public const string Status = "status";
        public const string NoStore = CacheControl.Keys.NoStore;
    }
}
