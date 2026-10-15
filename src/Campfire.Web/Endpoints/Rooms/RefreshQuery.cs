namespace Campfire.Web.Endpoints.Rooms;

/// <summary><c>params[:since]</c> of a room refresh: the client's last update, in Unix milliseconds.</summary>
public sealed record RefreshQuery( long Since );
