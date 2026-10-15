namespace Campfire.Web.Paths;

/// <summary>
/// The route templates the endpoints register (<c>MapGet</c>, <c>MapGroup</c> and the like), grouped by resource as
/// <c>reference/config/routes.rb</c> declares them. Templates carry route tokens (<c>{id:long}</c>); a template without
/// tokens is the same path a link uses, so it is the <see cref="Routes"/> constant. To build a URL to link or redirect
/// to, use <see cref="Routes"/>.
/// </summary>
public static partial class EndpointPaths
{
}
