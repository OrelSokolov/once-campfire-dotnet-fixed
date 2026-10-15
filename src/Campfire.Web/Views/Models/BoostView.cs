using Campfire.Web.Paths;
using Campfire.Web.Views.Helpers;

namespace Campfire.Web.Views.Models;

/// <summary>A boost.</summary>
/// <param name="Id">The boost's id.</param>
/// <param name="UpdatedAt">For the fragment key (<c>boost.cache_key_with_version</c>).</param>
/// <param name="MessageId">The boosted message.</param>
/// <param name="Content">The boost text.</param>
/// <param name="AllEmoji"><c>boost.content.all_emoji?</c>.</param>
/// <param name="Booster">Who boosted.</param>
public sealed record BoostView( long Id, DateTimeOffset UpdatedAt, long MessageId, string Content, bool AllEmoji, UserView Booster )
{
    /// <summary><c>dom_id(boost)</c>.</summary>
    public string DomId => DomIds.Boost( Id );

    public string Path => Routes.MessageBoost( MessageId, Id );
}
