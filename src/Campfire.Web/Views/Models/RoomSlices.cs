namespace Campfire.Web.Views.Models;

/// <summary>A persisted room.</summary>
/// <param name="Id">The room's id.</param>
/// <param name="Kind">The room's type.</param>
/// <param name="Name">The room's name (nil for direct rooms).</param>
/// <param name="DisplayName"><c>room_display_name(room)</c> for <c>Current.user</c>.</param>
public sealed record RoomView( long Id, RoomKind Kind, string? Name, string DisplayName );

/// <summary><c>rooms/show</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Room">The room.</param>
/// <param name="UpdatedAt"><c>room.updated_at</c>, the refresh controller's <c>loaded_at</c>.</param>
/// <param name="User"><c>Current.user</c>, for the client-side message template.</param>
/// <param name="Messages">The page of messages; each a cached fragment or a view.</param>
/// <param name="Invitation"><c>@room == Room.original &amp;&amp; !@room.messages.paged?</c> (<c>rooms/show/_invitation</c>).</param>
/// <param name="JoinCode"><c>Current.account.join_code</c>, for the invitation's join link.</param>
/// <param name="MessagesStreamName"><c>Turbo::StreamsChannel.signed_stream_name([ room, :messages ])</c>.</param>
public sealed record RoomsShowPage(
    ViewContext ViewContext,
    RoomView Room,
    DateTimeOffset UpdatedAt,
    UserView User,
    IReadOnlyList<MessageItem> Messages,
    bool Invitation,
    string JoinCode,
    string MessagesStreamName ) : IViewModel;

/// <summary><c>rooms/show/_nav</c> (its <c>content_for :nav</c>).</summary>
public sealed record RoomNavPartial( ViewContext ViewContext, RoomView Room ) : IViewModel;

/// <summary><c>rooms/show/_composer</c> (its <c>content_for :footer</c>).</summary>
public sealed record ComposerPartial( ViewContext ViewContext, RoomView Room ) : IViewModel;

/// <summary><c>rooms/show/_invitation</c>, shown when <paramref name="Show"/> holds.</summary>
public sealed record InvitationPartial( ViewContext ViewContext, bool Show, string JoinCode ) : IViewModel;

/// <summary><c>rooms/involvements/_bell</c>.</summary>
public sealed record BellPartial( ViewContext ViewContext, RoomView Room ) : IViewModel;

/// <summary><c>rooms/involvements/show</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="RoomId">The room's id.</param>
/// <param name="Kind">The room's type.</param>
/// <param name="Involvement">"mentions", "everything", "nothing" or "invisible".</param>
public sealed record InvolvementPage( ViewContext ViewContext, long RoomId, RoomKind Kind, string Involvement ) : IViewModel;

/// <summary>What <c>rooms/refreshes/show.turbo_stream</c> streams: messages created and updated since the client loaded.</summary>
public sealed record RoomsRefreshTurboStreamPage(
    ViewContext ViewContext,
    long RoomId,
    RoomKind RoomKind,
    IReadOnlyList<MessageItem> NewMessages,
    IReadOnlyList<MessageItem> UpdatedMessages ) : IViewModel;

/// <summary>The room being created or edited by the open and closed room forms; <paramref name="Id"/> is null for a new one.</summary>
public sealed record FormRoom( long? Id, string? Name );

/// <summary><c>rooms/opens/{new,edit}</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Room">The room.</param>
/// <param name="CanAdminister"><c>Current.user.can_administer?(room)</c>: administrators, the creator, or a new room.</param>
/// <param name="Users"><c>User.active.ordered</c>.</param>
public sealed record OpenRoomFormPage( ViewContext ViewContext, FormRoom Room, bool CanAdminister, IReadOnlyList<UserView> Users ) : IViewModel;

/// <summary><c>rooms/closeds/{new,edit}</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Room">The room.</param>
/// <param name="CanAdminister"><c>Current.user.can_administer?(room)</c>.</param>
/// <param name="CurrentUserId"><c>Current.user.id</c>.</param>
/// <param name="SelectedUsers">Active users with access (none for a new room).</param>
/// <param name="UnselectedUsers">The other active users.</param>
public sealed record ClosedRoomFormPage(
    ViewContext ViewContext,
    FormRoom Room,
    bool CanAdminister,
    long CurrentUserId,
    IReadOnlyList<UserView> SelectedUsers,
    IReadOnlyList<UserView> UnselectedUsers ) : IViewModel;

/// <summary><c>rooms/layouts/_new</c> and <c>_edit</c>: the page layouts around the room forms.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Room">The room.</param>
/// <param name="CanAdminister">Whether the delete button shows (<c>_edit</c>).</param>
public sealed record RoomPageLayout( ViewContext ViewContext, FormRoom Room, bool CanAdminister ) : IViewModel;

/// <summary><c>render layout: "rooms/layouts/form", locals: { room: } do ... end</c>: <paramref name="Body"/> writes the block.</summary>
public sealed record RoomFormLayout( ViewContext ViewContext, FormRoom Room, bool CanAdminister, RoomKind Kind, Func<ValueTask> Body ) : IViewModel;

/// <summary><c>rooms/opens/_form</c>.</summary>
public sealed record OpenRoomFormPartial( ViewContext ViewContext, OpenRoomFormPage Form, string TypeChangePath ) : IViewModel;

/// <summary><c>rooms/opens/_user</c>.</summary>
public sealed record OpenRoomUserPartial( ViewContext ViewContext, UserView User, bool CanAdminister ) : IViewModel;

/// <summary><c>rooms/closeds/_form</c>.</summary>
public sealed record ClosedRoomFormPartial( ViewContext ViewContext, ClosedRoomFormPage Form, string TypeChangePath ) : IViewModel;

/// <summary><c>rooms/closeds/_user</c>.</summary>
public sealed record ClosedRoomUserPartial( ViewContext ViewContext, ClosedRoomFormPage Form, UserView User, bool Selected ) : IViewModel;

/// <summary><c>rooms/directs/new</c>.</summary>
public sealed record DirectsNewPage( ViewContext ViewContext ) : IViewModel;

/// <summary><c>rooms/directs/edit</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="RoomId">The room's id.</param>
/// <param name="DisplayName"><c>room_display_name(@room)</c> for <c>Current.user</c>.</param>
/// <param name="Users"><c>@room.users.many? ? @room.users.without(Current.user) : @room.users</c>.</param>
public sealed record DirectsEditPage( ViewContext ViewContext, long RoomId, string DisplayName, IReadOnlyList<UserView> Users ) : IViewModel;

/// <summary><c>searches/index</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Query"><c>@query</c>: the sanitized search, when there is one.</param>
/// <param name="SubmittedQuery"><c>params[:q]</c> as submitted, the search field's value.</param>
/// <param name="Messages"><c>Current.user.reachable_messages.search(query).last(100)</c>.</param>
/// <param name="RecentSearches"><c>Current.user.searches.ordered.pluck(:query)</c>.</param>
/// <param name="ReturnToRoomId"><c>last_room_visited.id</c>, where the exit button goes.</param>
public sealed record SearchesPage(
    ViewContext ViewContext,
    string? Query,
    string? SubmittedQuery,
    IReadOnlyList<MessageItem> Messages,
    IReadOnlyList<string> RecentSearches,
    long ReturnToRoomId ) : IViewModel;
