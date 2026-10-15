using System.Globalization;
using Campfire.Web.Cable;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// turbo-rails helpers (<c>frames_helper.rb</c>, <c>streams_helper.rb</c>, <c>streams/action_helper.rb</c> and
/// <c>drive_helper.rb</c> at <c>30cd8fc</c>; Rust <c>helpers/turbo.rs</c>) and <c>dom_id</c>.
/// </summary>
public static class TurboHelper
{
    /// <summary>
    /// The opening tag of <c>turbo_frame_tag(id, src:, target:, **attributes) do ... end</c>:
    /// <c>attributes.merge(id:, src:, target:).compact</c>, so the attributes come first. The template writes
    /// <c>&lt;/turbo-frame&gt;</c>.
    /// </summary>
    public static SafeHtml TurboFrameTagOpen( string id, TagAttributes? attributes = null, string? sourceUrl = null, string? target = null )
    {
        var options = (attributes ?? new TagAttributes( )).Id( id ).Attribute( "src", sourceUrl ).Attribute( "target", target );
        return TagHelper.OpenTag( "turbo-frame", options );
    }

    /// <summary><c>turbo_frame_tag</c> around already-rendered content.</summary>
    public static SafeHtml TurboFrameTag( string id, SafeHtml content, TagAttributes? attributes = null, string? sourceUrl = null, string? target = null )
    {
        ArgumentNullException.ThrowIfNull( content );

        return SafeHtml.Raw( string.Concat( TurboFrameTagOpen( id, attributes, sourceUrl, target ).Html, content.Html, "</turbo-frame>" ) );
    }

    /// <summary>
    /// <c>turbo_stream_from(*streamables)</c>. The signed stream name comes from the caller
    /// (<c>Turbo::StreamsChannel.signed_stream_name</c>), and <paramref name="channel"/> is
    /// <c>Turbo::StreamsChannel</c> unless the call names one.
    /// </summary>
    public static SafeHtml TurboStreamFrom( string signedStreamName, string channel = TurboStreamsChannel.ClassName ) =>
        TagHelper.BuilderTag(
            "turbo-cable-stream-source",
            new TagAttributes( ).Attribute( "channel", channel ).Attribute( "signed-stream-name", signedStreamName ) );

    /// <summary><c>turbo_page_requires_reload_tag</c>, which <c>turbo_page_requires_reload</c> provides to <c>:head</c>.</summary>
    public static SafeHtml TurboPageRequiresReloadTag( ) =>
        TagHelper.BuilderTag( "meta", new TagAttributes( ).Name( "turbo-visit-control" ).Attribute( "content", "reload" ) );

    /// <summary><c>turbo_exempts_page_from_preview_tag</c>.</summary>
    public static SafeHtml TurboExemptsPageFromPreviewTag( ) =>
        TagHelper.BuilderTag( "meta", new TagAttributes( ).Name( "turbo-cache-control" ).Attribute( "content", "no-preview" ) );

    /// <summary>
    /// <c>turbo_stream_action_tag(action, target:, template:, **attributes)</c>, what both <c>turbo_stream.&lt;action&gt;</c>
    /// and the <c>broadcast_*_to</c> helpers emit: extra attributes first, then <c>action</c> and <c>target</c>;
    /// <c>remove</c> and <c>refresh</c> have no template element.
    /// </summary>
    public static SafeHtml TurboStreamActionTag( string action, string target, SafeHtml? template = null, TagAttributes? attributes = null )
    {
        var content = action is TurboStreamActions.Remove or TurboStreamActions.Refresh
            ? SafeHtml.Empty
            : TagHelper.ContentTag( "template", null, template ?? SafeHtml.Empty );
        var options = (attributes ?? new TagAttributes( )).Attribute( "action", action ).Attribute( "target", target );
        return TagHelper.ContentTag( "turbo-stream", options, content );
    }

    /// <summary>The opening half of a block-form <c>turbo_stream.&lt;action&gt; target do ... end</c>.</summary>
    public static SafeHtml TurboStreamOpen( string action, string target, TagAttributes? attributes = null ) =>
        SafeHtml.Concat(
            TagHelper.OpenTag( "turbo-stream", (attributes ?? new TagAttributes( )).Attribute( "action", action ).Attribute( "target", target ) ),
            SafeHtml.Raw( "<template>" ) );

    /// <summary>The closing half of <see cref="TurboStreamOpen"/>.</summary>
    public static SafeHtml TurboStreamClose( ) => SafeHtml.Raw( "</template></turbo-stream>" );

    /// <summary><c>dom_id(record, prefix)</c>: <c>prefix_model_id</c>.</summary>
    public static string DomId( string modelParamKey, string id, string? prefix = null ) =>
        string.IsNullOrEmpty( prefix ) ? $"{modelParamKey}_{id}" : $"{prefix}_{modelParamKey}_{id}";

    /// <summary><c>dom_id(record, prefix)</c> for a numeric id.</summary>
    public static string DomId( string modelParamKey, long id, string? prefix = null ) =>
        DomId( modelParamKey, id.ToString( CultureInfo.InvariantCulture ), prefix );
}
