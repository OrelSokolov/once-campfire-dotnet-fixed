namespace Campfire.Web.Views.Models;

/// <summary><c>messages/_message</c>.</summary>
public sealed record MessagePartial( ViewContext ViewContext, MessageView Message ) : IViewModel;

/// <summary><c>messages/_actions</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Message">The message.</param>
/// <param name="Url"><c>room_at_message_url(message.room, message)</c>.</param>
public sealed record MessageActionsPartial( ViewContext ViewContext, MessageView Message, string Url ) : IViewModel;

/// <summary><c>messages/_presentation</c>, which <c>MessagesController#update</c> also broadcasts.</summary>
public sealed record PresentationPartial( ViewContext ViewContext, MessageView Message ) : IViewModel;

/// <summary><c>messages/_template</c>: the client-side template for messages being sent, as <c>Current.user</c>.</summary>
public sealed record MessageTemplatePartial( ViewContext ViewContext, UserView User ) : IViewModel;

/// <summary><c>messages/_unrenderable</c>.</summary>
public sealed record UnrenderablePartial( ViewContext ViewContext ) : IViewModel;

/// <summary><c>messages/boosts/_boosts</c>.</summary>
public sealed record BoostsPartial( ViewContext ViewContext, MessageView Message ) : IViewModel;

/// <summary><c>messages/boosts/_boost</c>, which the boosts controller also broadcasts on its own.</summary>
public sealed record BoostPartial( ViewContext ViewContext, BoostView Boost ) : IViewModel;

/// <summary><c>messages/index</c>: the page of messages the client fetches while scrolling (no layout).</summary>
public sealed record MessagesIndexPage( ViewContext ViewContext, IReadOnlyList<MessageItem> Messages ) : IViewModel;

/// <summary><c>messages/show</c>: the message partial, inside the application layout.</summary>
public sealed record MessagesShowPage( ViewContext ViewContext, MessageItem Message ) : IViewModel;

/// <summary><c>messages/edit</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Message">The message.</param>
/// <param name="EditableBody">The editor's <c>value</c>: <c>editable_body(message)</c> as HTML, from the rich-text component.</param>
public sealed record MessagesEditPage( ViewContext ViewContext, MessageView Message, string? EditableBody ) : IViewModel;

/// <summary><c>messages/room_not_found</c>.</summary>
public sealed record RoomNotFoundPage( ViewContext ViewContext ) : IViewModel;

/// <summary><c>messages/create.turbo_stream</c>: appends the new message to its room's list (also <c>broadcast_create</c>).</summary>
public sealed record MessagesCreateTurboStreamPage( ViewContext ViewContext, MessageItem Message, RoomKind RoomKind ) : IViewModel;

/// <summary><c>messages/destroy.turbo_stream</c> (also <c>broadcast_remove</c>).</summary>
public sealed record MessagesDestroyTurboStreamPage( ViewContext ViewContext, string ClientMessageId ) : IViewModel;

/// <summary><c>messages/boosts/index</c>.</summary>
public sealed record BoostsIndexPage( ViewContext ViewContext, MessageView Message ) : IViewModel;

/// <summary><c>messages/boosts/new</c>, as <c>Current.user</c>.</summary>
public sealed record BoostsNewPage( ViewContext ViewContext, MessageView Message, UserView User ) : IViewModel;

/// <summary>A partial that needs only the view context (<c>layouts/_lightbox</c>, the PWA help partials, ...).</summary>
public sealed record ContextPartial( ViewContext ViewContext ) : IViewModel;
