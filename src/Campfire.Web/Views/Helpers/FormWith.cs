using Campfire.Web.Http;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>form_with(url:, model:, method:, id:, class:, data:)</c> and its <c>FormBuilder</c> fields. Rails learns about
/// <c>multipart</c> from a <c>file_field</c> inside the block before it writes the form tag; here a form with a file
/// field declares <see cref="Multipart"/> before <see cref="Open"/>, and a file field on an opened non-multipart
/// form throws rather than render a form that can't upload.
/// </summary>
public sealed class FormWith
{
    private readonly ViewContext _viewContext;
    private readonly string _action;
    private readonly TagAttributes _data = new( );
    private readonly FormState _state;
    private string _method = "post";
    private string? _objectName;
    private string? _id;
    private string? _cssClass;

    internal FormWith( ViewContext viewContext, string action )
        : this( viewContext, action, new FormState( ) )
    {
    }

    private FormWith( ViewContext viewContext, string action, FormState state )
    {
        ArgumentNullException.ThrowIfNull( viewContext );
        ArgumentNullException.ThrowIfNull( action );

        _viewContext = viewContext;
        _action = action;
        _state = state;
    }

    /// <summary><c>model:</c>: the param key fields are scoped under ("user", "account").</summary>
    public FormWith Model( string paramKey )
    {
        _objectName = paramKey;
        return this;
    }

    /// <summary><c>method:</c>; a persisted <c>model:</c> implies "patch", so pass it for those too.</summary>
    public FormWith Method( string value )
    {
        _method = value;
        return this;
    }

    public FormWith Id( string value )
    {
        _id = value;
        return this;
    }

    public FormWith Class( string value )
    {
        _cssClass = value;
        return this;
    }

    public FormWith Data( string key, TagAttributeValue? value )
    {
        _data.Data( key, value );
        return this;
    }

    /// <summary><c>auto_submit_form_with</c>: prepends the auto-submit Stimulus controller.</summary>
    public FormWith AutoSubmit( )
    {
        var existing = _data.GetText( "data-controller" ) ?? string.Empty;
        _data.Attribute( "data-controller", ("auto-submit " + existing).Trim( ) );
        return this;
    }

    /// <summary><c>multipart: true</c>, implied by a file field.</summary>
    public FormWith Multipart( )
    {
        if ( _state is { Opened: true, Multipart: false } )
        {
            throw new InvalidOperationException( "Declare Multipart() before Open() on a form with file fields." );
        }

        _state.Multipart = true;
        return this;
    }

    /// <summary><c>&lt;form ...&gt;</c> plus the <c>_method</c> and <c>authenticity_token</c> fields
    /// (<c>html_options_for_form_with</c> + <c>extra_tags_for_form</c>).</summary>
    public SafeHtml Open( )
    {
        _state.Opened = true;
        var html = new TagAttributes( ).Attribute( "id", _id ).Attribute( "class", _cssClass ).Merge( _data );
        if ( _state.Multipart )
        {
            html.Attribute( "enctype", ContentTypes.MultipartForm );
        }

        html.Attribute( "action", _action ).Attribute( "accept-charset", "UTF-8" );
        var lowered = _method.ToLowerInvariant( );
        SafeHtml extra;
        switch ( lowered )
        {
            case "get":
                html.Method( "get" );
                extra = SafeHtml.Empty;
                break;
            case "post" or "":
                html.Method( "post" );
                extra = FormHelper.TokenTag( _viewContext );
                break;
            default:
                html.Method( "post" );
                extra = FormHelper.MethodTag( lowered ) + FormHelper.TokenTag( _viewContext );
                break;
        }

        return TagHelper.OpenTag( "form", html ) + extra;
    }

    /// <summary>The whole form around already-rendered <paramref name="content"/> (a block-less <c>form_with</c> passes none).</summary>
    public SafeHtml Wrap( SafeHtml? content = null ) => SafeHtml.Concat( Open( ), content ?? SafeHtml.Empty, SafeHtml.Raw( "</form>" ) );

    public SafeHtml TextField( string field, string? value, TagAttributes? options = null ) => InputField( "text", field, value, options );

    public SafeHtml EmailField( string field, string? value, TagAttributes? options = null ) => InputField( "email", field, value, options );

    public SafeHtml UrlField( string field, string? value, TagAttributes? options = null ) => InputField( "url", field, value, options );

    public SafeHtml HiddenField( string field, string? value, TagAttributes? options = null ) => InputField( "hidden", field, value, options );

    /// <summary>Password fields never render the model's value (<c>{ value: nil }.merge!(options)</c>).</summary>
    public SafeHtml PasswordField( string field, TagAttributes? options = null )
    {
        var merged = new TagAttributes( );
        merged.Attribute( "value", null );
        return InputField( "password", field, null, merged.Merge( options ) );
    }

    public SafeHtml FileField( string field, TagAttributes? options = null )
    {
        Multipart( );
        return InputField( "file", field, null, options );
    }

    /// <summary><c>Tags::TextArea#render</c>: the value is the element's content, after a newline.</summary>
    public SafeHtml TextArea( string field, string? value, TagAttributes? options = null )
    {
        options ??= new TagAttributes( );
        AddDefaultNameAndId( field, options );
        var explicitValue = options.Has( "value" );
        var content = explicitValue ? options.Remove( "value" )?.Text : value;
        return TagHelper.ContentTag( "textarea", options, SafeHtml.Text( content ) );
    }

    /// <summary><c>form.check_box(field, options, checked_value, unchecked_value)</c>; <paramref name="current"/> is the
    /// model's value, compared with <paramref name="checkedValue"/>.</summary>
    public SafeHtml CheckBox( string field, TagAttributes? options, string checkedValue, string uncheckedValue, string? current )
    {
        options ??= new TagAttributes( );
        options.Attribute( "type", "checkbox" );
        options.Attribute( "value", checkedValue );
        if ( current == checkedValue )
        {
            options.Attribute( "checked", "checked" );
        }

        AddDefaultNameAndId( field, options );
        var hidden = new TagAttributes( );
        foreach ( var key in new[] { "name", "disabled", "form" } )
        {
            if ( options.Has( key ) )
            {
                hidden.Attribute( key, options.Get( key ) );
            }
        }

        hidden.Type( "hidden" ).Value( uncheckedValue );
        return TagHelper.LegacyTag( "input", hidden ) + TagHelper.LegacyTag( "input", options );
    }

    /// <summary><c>form.button(options) do ... end</c>'s opening tag.</summary>
    public SafeHtml ButtonOpen( TagAttributes? options ) => FormHelper.ButtonTagOpen( options );

    /// <summary><c>form.fields_for(:settings)</c>: fields named <c>object[settings][...]</c>.</summary>
    public FormWith FieldsFor( string name )
    {
        var nested = new FormWith( _viewContext, _action, _state ) { _objectName = $"{_objectName}[{name}]", _method = _method };
        return nested;
    }

    private SafeHtml InputField( string fieldType, string field, string? value, TagAttributes? options )
    {
        options ??= new TagAttributes( );
        if ( !options.Has( "size" ) )
        {
            options.Attribute( "size", options.Get( "maxlength" ) );
        }

        options.SetDefault( "type", fieldType );
        if ( fieldType != "file" )
        {
            options.FetchOrSet( "value", value );
        }

        AddDefaultNameAndId( field, options );
        return TagHelper.LegacyTag( "input", options );
    }

    private void AddDefaultNameAndId( string field, TagAttributes options )
    {
        options.FetchOrSet( "name", string.IsNullOrEmpty( _objectName ) ? field : $"{_objectName}[{field}]" );
        options.FetchOrSet( "id", string.IsNullOrEmpty( _objectName ) ? field : $"{FormHelper.SanitizeObjectName( _objectName )}_{field}" );
    }

    /// <summary>Shared by a form and its <c>fields_for</c> builders.</summary>
    private sealed class FormState
    {
        public bool Multipart { get; set; }

        public bool Opened { get; set; }
    }
}
