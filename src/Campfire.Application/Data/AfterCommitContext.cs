using Campfire.Application.Data.Hooks;

namespace Campfire.Application.Data;

/// <summary>
/// What an after-commit step runs with: the same leased connection, now outside any transaction, so each statement
/// commits on its own, as Rails' callbacks' <c>update_all</c> and <c>insert_all</c> do.
/// </summary>
public sealed class AfterCommitContext
{
    internal AfterCommitContext( CampfireDb db, TimeProvider clock, CampfireHooks hooks )
    {
        Db = db;
        Clock = clock;
        Hooks = hooks;
    }

    public CampfireDb Db { get; }

    public TimeProvider Clock { get; }

    public CampfireHooks Hooks { get; }

    /// <summary><c>Time.current</c>, as on <see cref="WriteScope.Now"/>.</summary>
    public DateTime Now => RailsTimestamp.Truncate( Clock.GetUtcNow( ).UtcDateTime );
}
