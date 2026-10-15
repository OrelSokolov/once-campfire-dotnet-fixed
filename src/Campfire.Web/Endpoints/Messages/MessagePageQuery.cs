namespace Campfire.Web.Endpoints.Messages;

/// <summary><c>params[:before]</c> and <c>params[:after]</c> of the messages page, as message ids.</summary>
public sealed record MessagePageQuery( long? Before, long? After );
