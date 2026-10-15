using Campfire.Application.Integrations.Search;

namespace Campfire.Web.Endpoints.Searches;

/// <summary>What <c>set_messages</c> found, and the <c>params[:q]</c> it read.</summary>
/// <param name="SubmittedQuery"><c>params[:q]</c> as submitted (the search field's value).</param>
/// <param name="Results"><c>@messages</c> and the sanitized query.</param>
public sealed record CurrentSearch( string? SubmittedQuery, SearchResults Results );
