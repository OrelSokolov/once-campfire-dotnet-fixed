using Campfire.Application.Messages;

namespace Campfire.Application.Integrations.Search;

/// <summary>What <c>SearchesController</c>'s <c>before_action :set_messages</c> found.</summary>
/// <param name="Query"><c>@query</c>: the sanitized query when it's present, else null.</param>
/// <param name="Messages"><c>@messages</c>: the newest 100 matches in the user's rooms, oldest first.</param>
public sealed record SearchResults( string? Query, IReadOnlyList<Message> Messages );
