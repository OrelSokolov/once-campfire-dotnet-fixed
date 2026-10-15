using Campfire.Web.Paths;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>ApplicationHelper</c>, <c>CableHelper</c>, <c>VersionHelper</c>, <c>TimeHelper</c>, <c>ClipboardHelper</c>,
/// <c>DropTargetHelper</c>, <c>QrCodeHelper</c>, <c>Users::ProfilesHelper#web_share_session_button</c> and
/// <c>csrf_meta_tags</c> (<c>reference/app/helpers/*.rb</c>; Rust <c>helpers/application.rs</c>).
/// </summary>
public static class ApplicationHelper
{
    /// <summary><c>page_title_tag</c>: <c>@page_title || "Campfire"</c>.</summary>
    public static SafeHtml PageTitleTag( string? pageTitle ) => TagHelper.ContentTagText( "title", null, pageTitle ?? "Campfire" );

    /// <summary><c>csrf_meta_tags</c>; nothing outside a request.</summary>
    public static SafeHtml CsrfMetaTags( ViewContext viewContext )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        if ( !viewContext.IncludeCsrfTags )
        {
            return SafeHtml.Empty;
        }

        var csrfParam = TagHelper.LegacyTag( "meta", new TagAttributes( ).Name( "csrf-param" ).Attribute( "content", string.Empty ) );
        var token = TagHelper.LegacyTag( "meta", new TagAttributes( ).Name( "csrf-token" ).Attribute( "content", string.Empty ) );
        return SafeHtml.Join( [csrfParam, token], "\n" );
    }

    /// <summary><c>csp_meta_tag</c>: nothing, since the reference configures no content security policy.</summary>
    public static SafeHtml CspMetaTag( ) => SafeHtml.Empty;

    /// <summary><c>current_user_meta_tags</c>.</summary>
    public static SafeHtml CurrentUserMetaTags( ViewContext viewContext )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        if ( viewContext.CurrentUser is not { } user )
        {
            return SafeHtml.Empty;
        }

        return TagHelper.LegacyTag( "meta", new TagAttributes( ).Name( "current-user-id" ).Attribute( "content", user.Id ) )
            + TagHelper.LegacyTag( "meta", new TagAttributes( ).Name( "current-user-name" ).Attribute( "content", user.Name ) );
    }

    /// <summary><c>script_aware_action_cable_meta_tag</c>.</summary>
    public static SafeHtml ScriptAwareActionCableMetaTag( ViewContext viewContext ) =>
        TagHelper.BuilderTag( "meta", new TagAttributes( ).Name( "action-cable-url" ).Attribute( "content", viewContext.CableUrl ) );

    /// <summary><c>custom_styles_tag</c>: the account's CSS, unescaped.</summary>
    public static SafeHtml CustomStylesTag( ViewContext viewContext )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        return viewContext.CustomStyles is { } styles
            ? TagHelper.ContentTag( "style", new TagAttributes( ).Data( "turbo_track", "reload" ), SafeHtml.Raw( styles ) )
            : SafeHtml.Empty;
    }

    /// <summary><c>body_classes</c>: <c>[ @body_class, admin_body_class, account_logo_body_class ].compact.join(" ")</c>.</summary>
    public static string BodyClasses( ViewContext viewContext, string? bodyClass )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        string?[] parts = [bodyClass, viewContext.CanAdminister ? "admin" : null, viewContext.Account.HasLogo ? "account-has-logo" : null];
        return string.Join( ' ', parts.OfType<string>( ) );
    }

    /// <summary><c>link_back</c>: to the referrer, unless it's missing or the current page.</summary>
    public static SafeHtml LinkBack( ViewContext viewContext )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        var backUrl = viewContext.Referrer is { } referrer && referrer != viewContext.RequestUrl ? referrer : Routes.Root;
        return LinkBackTo( viewContext, backUrl );
    }

    /// <summary><c>link_back_to(destination)</c>.</summary>
    public static SafeHtml LinkBackTo( ViewContext viewContext, string destination ) =>
        UrlHelper.LinkTo( destination, new TagAttributes( ).Class( "btn" ), IconWithScreenReaderLabel( viewContext, "arrow-left.svg", "Go Back" ) );

    /// <summary>
    /// What the icon buttons hold: <c>image_tag icon, aria: { hidden: "true" }, size: 20</c>, then
    /// <c>tag.span label, class: "for-screen-reader"</c> (with <c>id: labelId</c> when the button is labelled by it).
    /// </summary>
    public static SafeHtml IconWithScreenReaderLabel( ViewContext viewContext, string icon, string label, string? labelId = null )
    {
        var labelAttributes = new TagAttributes( ).Class( "for-screen-reader" );
        if ( labelId is not null )
        {
            labelAttributes.Id( labelId );
        }

        return AssetTagHelper.ImageTag( viewContext, icon, new TagAttributes( ).AriaHidden( ).Size( 20 ) )
            + TagHelper.ContentTagText( "span", labelAttributes, label );
    }

    /// <summary><c>RoomsHelper#link_back_to_last_room_visited</c>.</summary>
    public static SafeHtml LinkBackToLastRoomVisited( ViewContext viewContext )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        return LinkBackTo( viewContext, viewContext.LastRoomVisitedId is { } roomId ? Routes.Room( roomId ) : Routes.Root );
    }

    /// <summary><c>version_badge</c>.</summary>
    public static SafeHtml VersionBadge( ViewContext viewContext ) =>
        TagHelper.ContentTagText( "span", new TagAttributes( ).Class( "version-badge" ), viewContext.AppVersion );

    /// <summary><c>local_datetime_tag(datetime, style:, **attributes)</c>.</summary>
    public static SafeHtml LocalDatetimeTag( DateTimeOffset datetime, string style, TagAttributes? attributes = null )
    {
        var options = (attributes ?? new TagAttributes( )).Attribute( "datetime", RubyText.Iso8601( datetime ) ).Data( "local_time_target", style );
        return TagHelper.ContentTag( "time", options, SafeHtml.Empty );
    }

    /// <summary>The opening tag of <c>button_to_copy_to_clipboard(url) do ... end</c>; the template writes <c>&lt;/button&gt;</c>.</summary>
    public static SafeHtml ButtonToCopyToClipboardOpen( string url ) =>
        TagHelper.OpenTag(
            "button",
            new TagAttributes( ).Class( "btn" ).Data( "controller", "copy-to-clipboard" ).Data( "action", "copy-to-clipboard#copy" )
                .Data( "copy_to_clipboard_success_class", "btn--success" ).Data( "copy_to_clipboard_content_value", url ) );

    /// <summary><c>drop_target_actions</c>.</summary>
    public const string DropTargetActions = "dragenter->drop-target#dragenter dragover->drop-target#dragover drop->drop-target#drop";

    /// <summary>The Apple logo (U+F8FF, private use) the PWA help partials print for "the Apple menu".</summary>
    public const string AppleLogo = "";

    /// <summary>
    /// The opening tag of <c>link_to_zoom_qr_code(url) do ... end</c>: the QR code route takes the URL,
    /// base64url-encoded. The template writes <c>&lt;/a&gt;</c>.
    /// </summary>
    public static SafeHtml LinkToZoomQrCodeOpen( string url )
    {
        var path = Routes.QrCode( RubyText.UrlSafeBase64( url ) );
        var options = new TagAttributes( ).Class( "btn" ).Data( "lightbox_target", "image" ).Data( "action", "lightbox#open" )
            .Data( "lightbox_url_value", path );
        return UrlHelper.LinkToOpen( path, options );
    }

    /// <summary>The opening tag of <c>web_share_session_button(url, title, text) do ... end</c>.</summary>
    public static SafeHtml WebShareSessionButtonOpen( string url, string title, string text ) =>
        TagHelper.OpenTag(
            "button",
            new TagAttributes( ).Class( "btn" ).Hidden( ).Data( "controller", "web-share" ).Data( "action", "web-share#share" )
                .Data( "web_share_url_value", url ).Data( "web_share_text_value", text ).Data( "web_share_title_value", title ) );
}
