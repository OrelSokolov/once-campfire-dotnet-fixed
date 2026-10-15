using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary><c>TranslationsHelper</c>: the language popups beside form fields (Rust <c>helpers/translations.rs</c>).</summary>
public static class TranslationsHelper
{
    private const string PopupActions = "keydown.esc->popup#close toggle->popup#toggle click@document->popup#closeOnClickOutside";

    /// <summary><c>translations_for(key)</c>.</summary>
    public static SafeHtml TranslationsFor( string key )
    {
        if ( !TranslationsTable.Entries.TryGetValue( key, out var entries ) )
        {
            throw new ArgumentException( $"Unknown translation key {key}.", nameof( key ) );
        }

        var items = entries.Select( entry =>
            TagHelper.ContentTagText( "dt", null, entry.Language )
            + TagHelper.ContentTagText( "dd", new TagAttributes( ).Class( "margin-none" ), entry.Translation ) );
        return TagHelper.ContentTag( "dl", new TagAttributes( ).Class( "language-list" ), SafeHtml.Join( items ) );
    }

    /// <summary><c>translation_button(key)</c>.</summary>
    public static SafeHtml TranslationButton( ViewContext viewContext, string key )
    {
        var summary = TagHelper.ContentTag(
            "summary",
            new TagAttributes( ).Class( "btn" ).Tabindex( -1 ),
            AssetTagHelper.ImageTag( viewContext, "globe.svg", new TagAttributes( ).Size( 20 ).AriaHidden( ).Class( "color-icon" ) )
                + TagHelper.ContentTagText( "span", new TagAttributes( ).Class( "for-screen-reader" ), "Translate" ) );
        var menu = TagHelper.ContentTag(
            "div",
            new TagAttributes( ).Class( "language-list-menu shadow" ).Data( "popup_target", "menu" ),
            TranslationsFor( key ) );
        var details = new TagAttributes( ).Class( "position-relative" ).Data( "controller", "popup" ).Data( "action", PopupActions )
            .Data( "popup_orientation_top_class", "popup-orientation-top" );
        return TagHelper.ContentTag( "details", details, summary + menu );
    }
}
