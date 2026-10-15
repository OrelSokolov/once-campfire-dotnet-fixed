using Campfire.Web.Paths;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>RoomsHelper</c>, <c>Rooms::InvolvementsHelper</c>, <c>RichTextHelper</c> and the <c>MessagesHelper</c> tags
/// the room screen uses (Rust <c>helpers/rooms.rs</c> and <c>rooms/mod.rs</c>).
/// </summary>
public static class RoomsHelper
{
    /// <summary><c>rich_text_data_actions</c>.</summary>
    public const string RichTextDataActions = "lexxy:change->typing-notifications#start keydown->composer#submitByKeyboard:capture";

    private static readonly string[] SharedInvolvementOrder = ["mentions", "everything", "nothing", "invisible"];
    private static readonly string[] DirectInvolvementOrder = ["everything", "nothing"];

    /// <summary>
    /// <c>room_display_name(room, for_user:)</c>: a direct room is named after its other members
    /// (<c>to_sentence</c>), falling back to the viewer's own name when they're alone in it.
    /// </summary>
    public static string RoomDisplayName( string? name, bool direct, IReadOnlyList<string> otherMemberNames, string? forUserName )
    {
        ArgumentNullException.ThrowIfNull( otherMemberNames );

        if ( !direct )
        {
            return name ?? string.Empty;
        }

        var sentence = RubyText.ToSentence( otherMemberNames );
        return string.IsNullOrWhiteSpace( sentence ) ? forUserName ?? string.Empty : sentence;
    }

    /// <summary>
    /// The opening tag of <c>link_to_room(room, **attributes) do ... end</c>. <paramref name="options"/> is the attribute
    /// hash in Ruby order, <c>data-*</c> entries where the <c>data:</c> key was.
    /// </summary>
    public static SafeHtml LinkToRoomOpen( long roomId, TagAttributes options )
    {
        ArgumentNullException.ThrowIfNull( options );

        var defaults = new TagAttributes( ).Data( "rooms_list_target", "room" ).Data( "room_id", roomId ).Data( "badge_dot_target", "unread" )
            .Data( "sorted_list_target", "item" );
        return UrlHelper.LinkToOpen( Routes.Room( roomId ), options.WithDefaultData( defaults ) );
    }

    /// <summary><c>HUMANIZE_INVOLVEMENT</c>.</summary>
    public static string HumanizeInvolvement( string involvement ) => involvement switch
    {
        "mentions" => "Notifying about @ mentions",
        "everything" => "Notifying about all messages",
        "nothing" => "Notifications are off",
        "invisible" => "Notifications are off and room invisible in sidebar",
        _ => string.Empty,
    };

    /// <summary><c>next_involvement_for(room, involvement:)</c>.</summary>
    public static string NextInvolvement( bool direct, string involvement )
    {
        var order = direct ? DirectInvolvementOrder : SharedInvolvementOrder;
        var index = Array.IndexOf( order, involvement );
        return index >= 0 && index + 1 < order.Length ? order[index + 1] : order[0];
    }

    /// <summary><c>button_to_change_involvement(room, involvement)</c>.</summary>
    public static SafeHtml ButtonToChangeInvolvement( ViewContext viewContext, long roomId, RoomKind kind, string involvement )
    {
        var labelId = kind.DomId( roomId, "involvement_label" );
        var url = Routes.RoomInvolvement( roomId, NextInvolvement( kind.IsDirect( ), involvement ) );
        var content = ApplicationHelper.IconWithScreenReaderLabel(
            viewContext,
            $"notification-bell-{involvement}.svg",
            HumanizeInvolvement( involvement ),
            labelId );
        var options = new TagAttributes( ).Method( "put" ).Role( "checkbox" ).Aria( "checked", true ).Aria( "labelledby", labelId ).Tabindex( 0 )
            .Class( "btn " + involvement );
        return FormHelper.ButtonTo( viewContext, url, options ).Wrap( content );
    }

    /// <summary>The opening tag of <c>turbo_frame_for_involvement_tag(room) do ... end</c>.</summary>
    public static SafeHtml TurboFrameForInvolvementTagOpen( long roomId, RoomKind kind )
    {
        var data = new TagAttributes( ).Data( "controller", "turbo-frame" ).Data( "action", "notifications:ready@window->turbo-frame#load" )
            .Data( "turbo_frame_url_param", Routes.RoomInvolvement( roomId ) );
        return TurboHelper.TurboFrameTagOpen( DomIds.RoomInvolvementFrame( kind, roomId ), data );
    }

    /// <summary>
    /// <c>button_to_delete_room(room, url: nil)</c>: to <c>room_url(room)</c>, labelled with the room's name, showing its
    /// display name.
    /// </summary>
    public static SafeHtml ButtonToDeleteRoom( ViewContext viewContext, long roomId, string? roomName, string displayName )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        var content = AssetTagHelper.ImageTag( viewContext, "trash.svg", new TagAttributes( ).AriaHidden( ).Size( 20 ) )
            + TagHelper.ContentTagText( "span", new TagAttributes( ).Class( "overflow-ellipsis" ), displayName );
        var options = new TagAttributes( ).Method( "delete" ).Class( "btn btn--negative max-width" ).Aria( "label", "Delete " + roomName )
            .Data( "turbo_confirm", "Are you sure you want to delete this room and all messages in it? This can’t be undone." );
        return FormHelper.ButtonTo( viewContext, viewContext.Url( Routes.Room( roomId ) ), options ).Wrap( content );
    }

    /// <summary><c>button_to_jump_to_newest_message</c>.</summary>
    public static SafeHtml ButtonToJumpToNewestMessage( ViewContext viewContext )
    {
        var content = ApplicationHelper.IconWithScreenReaderLabel( viewContext, "arrow-down.svg", "Jump to newest message" );
        var options = new TagAttributes( ).Class( "message-area__return-to-latest btn" ).Data( "action", "messages#returnToLatest" )
            .Data( "messages_target", "latest" ).Hidden( );
        return TagHelper.ContentTag( "button", options, content );
    }

    /// <summary><c>submit_room_button_tag</c>.</summary>
    public static SafeHtml SubmitRoomButtonTag( ViewContext viewContext )
    {
        var content = ApplicationHelper.IconWithScreenReaderLabel( viewContext, "check.svg", "Save" );
        return FormHelper.ButtonTag( new TagAttributes( ).Class( "btn btn--reversed txt-large center" ).Type( "submit" ), content );
    }

    /// <summary><c>mention_prompt_tag(room)</c>.</summary>
    public static SafeHtml MentionPromptTag( long roomId ) =>
        TagHelper.BuilderTag(
            "lexxy-prompt",
            new TagAttributes( ).Attribute( "trigger", "@" ).Name( "mention" ).Attribute( "src", Routes.AutocompletableUsersForRoom( roomId ) )
                .Attribute( "remote-filtering", "true" ).Attribute( "empty-results", "No matches" ) );

    /// <summary>
    /// The opening tag of Lexxy's <c>form.rich_text_area field, **options do ... end</c>: the options, the direct-upload
    /// URLs (absolute), then <c>id</c>, <c>input</c>, <c>name</c> and, when given, <c>value</c>.
    /// </summary>
    public static SafeHtml RichTextAreaOpen(
        ViewContext viewContext,
        string objectName,
        string field,
        string recordDomId,
        TagAttributes options,
        string? value = null )
    {
        ArgumentNullException.ThrowIfNull( viewContext );
        ArgumentNullException.ThrowIfNull( options );

        options.Data( "direct_upload_url", viewContext.Url( "/rails/active_storage/direct_uploads" ) )
            .Data( "blob_url_template", viewContext.Url( "/rails/active_storage/blobs/redirect/:signed_id/:filename" ) )
            .Id( $"{objectName}_{field}" ).Attribute( "input", $"{objectName}_{field}_trix_input_{recordDomId}" ).Name( $"{objectName}[{field}]" )
            .Attribute( "value", value );
        return TagHelper.OpenTag( "lexxy-editor", options );
    }

    /// <summary>The opening tag of <c>link_to_edit_room(room) do ... end</c>.</summary>
    public static SafeHtml LinkToEditRoomOpen( long roomId, RoomKind kind ) =>
        UrlHelper.LinkToOpen(
            EditRoomPath( roomId, kind ),
            new TagAttributes( ).Class( "btn" ).Style( $"view-transition-name: edit-room-{roomId}" ).Data( "room_id", roomId ) );

    /// <summary><c>edit_polymorphic_path(room)</c>.</summary>
    public static string EditRoomPath( long roomId, RoomKind kind ) => kind switch
    {
        RoomKind.Open => Routes.EditRoomsOpen( roomId ),
        RoomKind.Closed => Routes.EditRoomsClosed( roomId ),
        _ => Routes.EditRoomsDirect( roomId ),
    };

    /// <summary>The opening tag of <c>message_area_tag(room) do ... end</c>.</summary>
    public static SafeHtml MessageAreaTagOpen( ViewContext viewContext, long roomId )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        const string actions =
            "turbo:before-stream-render@document->messages#beforeStreamRender keydown.up@document->messages#editMyLastMessage " +
            ApplicationHelper.DropTargetActions + " visibilitychange@document->presence#visibilityChanged";
        var options = new TagAttributes( ).Id( "message-area" ).Class( "message-area" ).Attribute( "contents", "true" )
            .Data( "controller", "messages presence drop-target" ).Data( "action", actions )
            .Data( "messages_first_of_day_class", "message--first-of-day" ).Data( "messages_formatted_class", "message--formatted" )
            .Data( "messages_me_class", "message--me" ).Data( "messages_mentioned_class", "message--mentioned" )
            .Data( "messages_threaded_class", "message--threaded" ).Data( "messages_page_url_value", viewContext.Url( Routes.RoomMessages( roomId ) ) );
        return TagHelper.OpenTag( "div", options );
    }

    /// <summary>The opening tag of <c>messages_tag(room) do ... end</c>.</summary>
    public static SafeHtml MessagesTagOpen( ViewContext viewContext, long roomId, RoomKind kind, DateTimeOffset roomUpdatedAt )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        const string actions =
            "turbo:before-stream-render@document->maintain-scroll#beforeStreamRender " +
            "visibilitychange@document->refresh-room#visibilityChanged online@window->refresh-room#online";
        var options = new TagAttributes( ).Id( DomIds.RoomMessages( kind, roomId ) ).Class( "messages" )
            .Data( "controller", "maintain-scroll refresh-room" ).Data( "action", actions ).Data( "messages_target", "messages" )
            .Data( "refresh_room_loaded_at_value", RubyText.EpochMilliseconds( roomUpdatedAt ) )
            .Data( "refresh_room_url_value", viewContext.Url( Routes.RoomRefresh( roomId ) ) );
        return TagHelper.OpenTag( "div", options );
    }

    /// <summary><c>composer_form_tag(room)</c>: <c>form_with model: Message.new, url: room_messages_path(room), id: "composer"</c>.</summary>
    public static FormWith ComposerFormTag( ViewContext viewContext, long roomId )
    {
        const string actions =
            ApplicationHelper.DropTargetActions + " drop-target:drop@window->composer#dropFiles " +
            "lexxy:file-accept->composer#preventAttachment refresh-room:online@window->composer#online typing-notifications#stop " +
            "paste->composer#pasteFiles turbo:submit-end->composer#submitEnd refresh-room:offline@window->composer#offline";
        return FormHelper.FormWith( viewContext, Routes.RoomMessages( roomId ) ).Model( "message" ).Id( "composer" )
            .Class( "margin-block flex-item-grow contain" ).Data( "controller", "composer drop-target" ).Data( "action", actions )
            .Data( "composer_messages_outlet", "#message-area" ).Data( "composer_toolbar_class", "composer--rich-text" )
            .Data( "composer_room_id_value", roomId );
    }

    /// <summary>The opening tag of <c>search_results_tag do ... end</c> (<c>SearchesHelper</c>).</summary>
    public static SafeHtml SearchResultsTagOpen( ) =>
        TagHelper.OpenTag(
            "div",
            new TagAttributes( ).Id( "search-results" ).Class( "messages searches__results" ).Data( "controller", "search-results" )
                .Data( "search_results_target", "messages" ).Data( "search_results_me_class", "message--me" )
                .Data( "search_results_threaded_class", "message--threaded" ).Data( "search_results_mentioned_class", "message--mentioned" )
                .Data( "search_results_formatted_class", "message--formatted" ) );
}
