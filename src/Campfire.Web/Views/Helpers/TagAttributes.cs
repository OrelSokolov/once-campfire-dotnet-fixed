namespace Campfire.Web.Views.Helpers;

/// <summary>
/// An ordered Ruby options hash for <c>ActionView::Helpers::TagHelper</c> (Rust <c>helpers/tag.rs</c>). Rails renders
/// attributes in hash order, so setters keep insertion order and a reassignment keeps the key's position. A
/// <see langword="null"/> value keeps its slot but never renders, like <c>options["value"] = nil</c>.
/// </summary>
public sealed class TagAttributes
{
    /// <summary><c>TagHelper::BOOLEAN_ATTRIBUTES</c>.</summary>
    private static readonly HashSet<string> BooleanAttributes = new( StringComparer.Ordinal )
    {
        "allowfullscreen", "allowpaymentrequest", "async", "autofocus", "autoplay", "checked", "compact", "controls",
        "declare", "default", "defaultchecked", "defaultmuted", "defaultselected", "defer", "disabled", "enabled",
        "formnovalidate", "hidden", "indeterminate", "inert", "ismap", "itemscope", "loop", "multiple", "muted", "nohref",
        "nomodule", "noresize", "noshade", "novalidate", "nowrap", "open", "pauseonexit", "playsinline", "readonly",
        "required", "reversed", "scoped", "seamless", "selected", "sortable", "truespeed", "typemustmatch", "visible",
    };

    private readonly List<KeyValuePair<string, TagAttributeValue?>> _entries = [];

    public IEnumerable<KeyValuePair<string, TagAttributeValue?>> Entries => _entries;

    /// <summary><c>options[name] = value</c>: sets <paramref name="name"/>, replacing an earlier value in place.</summary>
    public TagAttributes Attribute( string name, TagAttributeValue? value )
    {
        ArgumentNullException.ThrowIfNull( name );

        var index = IndexOf( name );
        if ( index >= 0 )
        {
            _entries[index] = new( name, value );
        }
        else
        {
            _entries.Add( new( name, value ) );
        }

        return this;
    }

    /// <summary><c>data: { key => value }</c>: <c>data-</c> plus the dasherized key. Non-strings go through
    /// <c>to_json</c>, so <see langword="true"/> renders as "true".</summary>
    public TagAttributes Data( string key, TagAttributeValue? value ) => Attribute( "data-" + Dasherize( key ), AsText( value ) );

    /// <summary><c>aria: { key => value }</c>.</summary>
    public TagAttributes Aria( string key, TagAttributeValue? value ) => Attribute( "aria-" + Dasherize( key ), AsText( value ) );

    /// <summary><c>aria: { hidden: "true" }</c>, the most common option in Campfire's views.</summary>
    public TagAttributes AriaHidden( ) => Aria( "hidden", "true" );

    public TagAttributes Class( TagAttributeValue? value ) => Attribute( "class", value );

    public TagAttributes Id( TagAttributeValue? value ) => Attribute( "id", value );

    public TagAttributes Style( TagAttributeValue? value ) => Attribute( "style", value );

    public TagAttributes Title( TagAttributeValue? value ) => Attribute( "title", value );

    public TagAttributes Alt( TagAttributeValue? value ) => Attribute( "alt", value );

    public TagAttributes Role( TagAttributeValue? value ) => Attribute( "role", value );

    public TagAttributes Name( TagAttributeValue? value ) => Attribute( "name", value );

    public TagAttributes Type( TagAttributeValue? value ) => Attribute( "type", value );

    public TagAttributes Value( TagAttributeValue? value ) => Attribute( "value", value );

    public TagAttributes Target( TagAttributeValue? value ) => Attribute( "target", value );

    public TagAttributes Placeholder( TagAttributeValue? value ) => Attribute( "placeholder", value );

    public TagAttributes Autocomplete( TagAttributeValue? value ) => Attribute( "autocomplete", value );

    public TagAttributes Accept( TagAttributeValue? value ) => Attribute( "accept", value );

    public TagAttributes Loading( TagAttributeValue? value ) => Attribute( "loading", value );

    public TagAttributes Tabindex( TagAttributeValue? value ) => Attribute( "tabindex", value );

    public TagAttributes Maxlength( TagAttributeValue? value ) => Attribute( "maxlength", value );

    public TagAttributes Rows( TagAttributeValue? value ) => Attribute( "rows", value );

    /// <summary><c>image_tag</c>'s <c>size:</c>, expanded into width and height by <see cref="AssetTagHelper.ImageTag"/>.</summary>
    public TagAttributes Size( TagAttributeValue? value ) => Attribute( "size", value );

    public TagAttributes Method( TagAttributeValue? value ) => Attribute( "method", value );

    public TagAttributes Hidden( bool value = true ) => Attribute( "hidden", value );

    public TagAttributes Required( bool value = true ) => Attribute( "required", value );

    public TagAttributes Autofocus( bool value = true ) => Attribute( "autofocus", value );

    public TagAttributes Readonly( bool value = true ) => Attribute( "readonly", value );

    public TagAttributes Disabled( bool value = true ) => Attribute( "disabled", value );

    public TagAttributes Checked( bool value = true ) => Attribute( "checked", value );

    /// <summary><c>options[name] ||= value</c>: sets only when absent or nil.</summary>
    public void SetDefault( string name, TagAttributeValue? value )
    {
        if ( Get( name ) is null )
        {
            Attribute( name, value );
        }
    }

    /// <summary><c>options.fetch(name) { value }</c>: sets only when the key is absent (an explicit nil stays nil).</summary>
    public void FetchOrSet( string name, TagAttributeValue? value )
    {
        if ( !Has( name ) )
        {
            Attribute( name, value );
        }
    }

    public TagAttributeValue? Get( string name )
    {
        var index = IndexOf( name );
        return index >= 0 ? _entries[index].Value : null;
    }

    public string? GetText( string name ) => Get( name )?.Text;

    public bool Has( string name ) => IndexOf( name ) >= 0;

    public TagAttributeValue? Remove( string name )
    {
        var index = IndexOf( name );
        if ( index < 0 )
        {
            return null;
        }

        var value = _entries[index].Value;
        _entries.RemoveAt( index );
        return value;
    }

    /// <summary>Appends <paramref name="other"/>'s entries, overriding in place like <c>Hash#merge!</c>.</summary>
    public TagAttributes Merge( TagAttributes? other )
    {
        if ( other is not null )
        {
            foreach ( var (name, value) in other._entries )
            {
                Attribute( name, value );
            }
        }

        return this;
    }

    public TagAttributes Clone( ) => new TagAttributes( ).Merge( this );

    /// <summary>
    /// <c>link_to(url, **attributes, data: defaults.merge(attributes.delete(:data)))</c>: the default data attributes
    /// go where the caller's first <c>data-*</c> attribute is (or at the end), ahead of the caller's own data
    /// attributes, which override them.
    /// </summary>
    public TagAttributes WithDefaultData( TagAttributes defaults )
    {
        ArgumentNullException.ThrowIfNull( defaults );

        var position = _entries.FindIndex( entry => entry.Key.StartsWith( "data-", StringComparison.Ordinal ) );
        if ( position < 0 )
        {
            position = _entries.Count;
        }

        var result = new TagAttributes( );
        var data = defaults.Clone( );
        var after = new TagAttributes( );
        for ( var index = 0; index < _entries.Count; index++ )
        {
            var (name, value) = _entries[index];
            if ( index < position )
            {
                result.Attribute( name, value );
            }
            else if ( name.StartsWith( "data-", StringComparison.Ordinal ) )
            {
                data.Attribute( name, value );
            }
            else
            {
                after.Attribute( name, value );
            }
        }

        return result.Merge( data ).Merge( after );
    }

    /// <summary><c>tag_options</c>: the rendered attributes, each with a leading space.</summary>
    public string Render( )
    {
        return string.Create( RenderedLength, this, static ( output, attributes ) =>
        {
            if ( attributes.RenderTo( output ) != output.Length )
            {
                throw new InvalidOperationException( "Attribute sizing and rendering disagree." );
            }
        } );
    }

    /// <summary>The length <see cref="RenderTo"/> writes.</summary>
    internal int RenderedLength
    {
        get
        {
            var length = 0;
            foreach ( var (name, value) in _entries )
            {
                if ( RenderedValue( name, value ) is { } text )
                {
                    length += 4 + name.Length + Escaping.EscapedLength( text, quotesOnly: value!.Kind == TagAttributeKind.Safe );
                }
            }

            return length;
        }
    }

    /// <summary>Writes each rendered attribute as <c> name="value"</c> and returns the characters written.</summary>
    internal int RenderTo( Span<char> output )
    {
        var position = 0;
        foreach ( var (name, value) in _entries )
        {
            if ( RenderedValue( name, value ) is not { } text )
            {
                continue;
            }

            output[position++] = ' ';
            name.CopyTo( output[position..] );
            position += name.Length;
            "=\"".CopyTo( output[position..] );
            position += 2;
            position += Escaping.WriteEscaped( output[position..], text, quotesOnly: value!.Kind == TagAttributeKind.Safe );
            output[position++] = '"';
        }

        return position;
    }

    /// <summary>
    /// The text an attribute renders, or <see langword="null"/> when it renders nothing: a nil value, or a false
    /// boolean attribute. A true boolean attribute renders its own name (<c>hidden="hidden"</c>).
    /// </summary>
    private static string? RenderedValue( string name, TagAttributeValue? value )
    {
        if ( value is null )
        {
            return null;
        }

        if ( value.Kind == TagAttributeKind.Bool && BooleanAttributes.Contains( name ) )
        {
            return value.Flag ? name : null;
        }

        return value.Text;
    }

    /// <summary><c>String#dasherize</c>.</summary>
    public static string Dasherize( string key )
    {
        ArgumentNullException.ThrowIfNull( key );

        return key.Replace( '_', '-' );
    }

    /// <summary>Booleans under <c>data:</c>/<c>aria:</c> render as the text "true"/"false", never as boolean attributes.</summary>
    private static TagAttributeValue? AsText( TagAttributeValue? value ) =>
        value is { Kind: TagAttributeKind.Bool } ? TagAttributeValue.Of( value.Text ) : value;

    private int IndexOf( string name )
    {
        for ( var index = 0; index < _entries.Count; index++ )
        {
            if ( string.Equals( _entries[index].Key, name, StringComparison.Ordinal ) )
            {
                return index;
            }
        }

        return -1;
    }
}
