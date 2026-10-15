using Campfire.Application.RichText;
using Campfire.Application.RichText.Html;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Ruby;
using Campfire.Application.Serialization;

using Microsoft.Extensions.Logging;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary>
/// <c>Opengraph::Metadata</c> and its <c>Fetching</c> concern (<c>app/models/opengraph/metadata.rb</c>,
/// <c>metadata/fetching.rb</c>). The attributes are kept as the model's instance variables, in the order they were
/// first assigned, which is the order <c>render json:</c> emits them.
/// </summary>
public sealed class OpengraphMetadata
{
    private static readonly string[] TwitterHosts = ["twitter.com", "www.twitter.com", "x.com", "www.x.com"];

    private static readonly string[] AllowedImageContentTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    private const string FxTwitterHost = "fxtwitter.com";

    private readonly List<KeyValuePair<string, string?>> _attributes;

    private OpengraphMetadata( List<KeyValuePair<string, string?>> attributes ) => _attributes = attributes;

    /// <summary>An attribute's value (<c>title</c>, <c>url</c>, <c>image</c> or <c>description</c>).</summary>
    public string? this[string key] => _attributes.ValueOf( key );

    /// <summary>
    /// <c>render json: opengraph</c> after <c>valid?</c>: <c>Object#as_json</c> of every instance variable, which by then
    /// include the validation context and the (empty) errors. The controller's <c>render json:</c> doesn't escape HTML
    /// (<see cref="JsonEscaping.None"/>); <c>to_json</c> does.
    /// </summary>
    public string ToJson( JsonEscaping escaping = JsonEscaping.None )
    {
        using var json = new JsonPayloadWriter( escaping );
        json.WriteStartObject( );
        foreach ( var (key, value) in _attributes )
        {
            json.WritePropertyName( key );
            json.WriteString( value );
        }

        json.WritePropertyName( "context_for_validation" );
        json.WriteStartObject( );
        json.WritePropertyName( "context" );
        json.WriteNull( );
        json.WriteEndObject( );
        json.WritePropertyName( "errors" );
        json.WriteStartObject( );
        json.WriteEndObject( );
        json.WriteEndObject( );
        return json.ToString( );
    }

    /// <summary><c>Metadata.from_url(url)</c>.</summary>
    internal static async Task<OpengraphMetadata> FromUrlAsync( OpengraphFetchContext fetchContext, string url, CancellationToken cancellationToken )
    {
        var body = await FetchDocumentAsync( fetchContext, url, cancellationToken );
        var found = await fetchContext.OffThreadAsync( ( ) => OpengraphDocument.OpengraphAttributes( body ), cancellationToken );

        var canonicalUrl = await ValidCanonicalUrlAsync( fetchContext, found.ValueOf( OpengraphAttributeNames.Url ), url, cancellationToken );
        var image = await ValidImageContentTypeAsync( fetchContext, found.ValueOf( OpengraphAttributeNames.Image ), cancellationToken );

        var attributes = found.Select( attribute => new KeyValuePair<string, string?>( attribute.Key, attribute.Value ) ).ToList( );
        Assign( attributes, OpengraphAttributeNames.Url, canonicalUrl );
        Assign( attributes, OpengraphAttributeNames.Image, image );
        return new OpengraphMetadata( attributes );
    }

    /// <summary>
    /// <c>valid?</c>: the title and description sanitized first (<c>before_validation</c>), then title, url and
    /// description present, and an image, if any, a valid location. Every validation runs.
    /// </summary>
    internal async Task<bool> ValidateAsync( OpengraphFetchContext fetchContext, CancellationToken cancellationToken )
    {
        var (title, description) = (this[OpengraphAttributeNames.Title], this[OpengraphAttributeNames.Description]);
        var (sanitizedTitle, sanitizedDescription) =
            await fetchContext.OffThreadAsync( ( ) => (Sanitize( title ), Sanitize( description )), cancellationToken );
        Assign( _attributes, OpengraphAttributeNames.Title, sanitizedTitle );
        Assign( _attributes, OpengraphAttributeNames.Description, sanitizedDescription );

        var valid = !RubyStrings.IsBlank( this[OpengraphAttributeNames.Title] )
            && !RubyStrings.IsBlank( this[OpengraphAttributeNames.Url] )
            && !RubyStrings.IsBlank( this[OpengraphAttributeNames.Description] );
        var image = this[OpengraphAttributeNames.Image];
        if ( !RubyStrings.IsBlank( image ) )
        {
            valid &= await new OpengraphLocation( fetchContext, image ).IsValidAsync( cancellationToken );
        }

        return valid;
    }

    /// <summary>
    /// <c>sanitize(strip_tags(value))</c>: <c>Rails::HTML5::FullSanitizer</c> (the text of the parsed fragment's
    /// non-comment nodes, escaped with <c>CGI.escapeHTML</c>), then <c>Rails::HTML5::SafeListSanitizer</c> with its
    /// defaults. Nokogiri's <c>ArgumentError</c> for markup past its limits raises out of the unfurl (a 500).
    /// </summary>
    internal static string? Sanitize( string? value )
    {
        if ( value is null )
        {
            return null;
        }

        try
        {
            return SafeListSanitizer.Sanitize( StripTags( value ), SafeList.Defaults );
        }
        catch ( RichTextException error )
        {
            throw new OpengraphUnfurlException( error.RubyClass, error );
        }
    }

    private static string StripTags( string value )
    {
        if ( value.Length == 0 )
        {
            return value;
        }

        var fragment = HtmlFragmentParser.ParseFragment( value );
        var text = string.Concat( fragment.Children.Where( node => node.Name != "comment" ).Select( node => node.TextContent( ) ) );
        return RubyStrings.HtmlEscape( text );
    }

    private static void Assign( List<KeyValuePair<string, string?>> attributes, string key, string? value )
    {
        var index = attributes.IndexOfKey( key );
        if ( index < 0 )
        {
            attributes.Add( new( key, value ) );
        }
        else
        {
            attributes[index] = new( key, value );
        }
    }

    /// <summary>
    /// <c>fetch_document(untrusted_url)</c>: tweets are read through fxtwitter.com. A tweet whose fxtwitter page can't be
    /// read raises (<c>nil.force_encoding</c>, <c>NoMethodError</c>), as does a URL <c>URI.parse</c> rejects with
    /// <c>URI::InvalidComponentError</c>.
    /// </summary>
    private static async Task<byte[]?> FetchDocumentAsync( OpengraphFetchContext fetchContext, string url, CancellationToken cancellationToken )
    {
        if ( !IsTweetUrl( url ) )
        {
            return await new OpengraphLocation( fetchContext, url ).ReadHtmlAsync( cancellationToken );
        }

        var html = await new OpengraphLocation( fetchContext, ReplaceTwitterDomain( url ) ).ReadHtmlAsync( cancellationToken );
        if ( html is null )
        {
            throw new OpengraphUnfurlException( RubyErrorClasses.NoMethodError );
        }

        return html;
    }

    /// <summary><c>tweet_url?</c>.</summary>
    private static bool IsTweetUrl( string url )
    {
        switch ( RubyUri.TryParse( url, out var uri ) )
        {
            case RubyUriError.InvalidUri:
                return false;
            case RubyUriError.InvalidComponent:
                throw new OpengraphUnfurlException( RubyErrorClasses.InvalidUriComponent );
        }

        return TwitterHosts.Contains( uri!.Host ) && !RubyStrings.IsBlank( uri.Path ) && uri.Path != "/";
    }

    /// <summary><c>replace_twitter_domain_for_opengraph_support</c>.</summary>
    private static string? ReplaceTwitterDomain( string url )
    {
        if ( RubyUri.TryParse( url, out var uri ) != RubyUriError.None )
        {
            return null;
        }

        if ( TwitterHosts.Contains( uri!.Host ) )
        {
            uri.Host = FxTwitterHost;
        }

        return uri.ToString( );
    }

    /// <summary><c>valid_canonical_url(url, fallback)</c>.</summary>
    private static async Task<string> ValidCanonicalUrlAsync(
        OpengraphFetchContext fetchContext, string? url, string fallback, CancellationToken cancellationToken ) =>
        await new OpengraphLocation( fetchContext, url ).IsValidAsync( cancellationToken ) ? url! : fallback;

    /// <summary><c>valid_image_content_type(image)</c>: kept only when a HEAD says it's a JPEG, PNG, GIF or WebP.</summary>
    private static async Task<string?> ValidImageContentTypeAsync(
        OpengraphFetchContext fetchContext, string? image, CancellationToken cancellationToken )
    {
        if ( RubyStrings.IsBlank( image ) )
        {
            return null;
        }

        if ( RubyUri.TryParse( image!, out _ ) != RubyUriError.None )
        {
            if ( fetchContext.Logger.IsEnabled( LogLevel.Warning ) )
            {
                // "tpye" (sic: Rails) is the reference's log text
                fetchContext.Logger.LogWarning( "Failed to fetch image content tpye: {Image} (bad URI(is not URI?))", image );
            }

            return null;
        }

        var contentType = await new OpengraphLocation( fetchContext, image ).FetchContentTypeAsync( cancellationToken );
        return contentType is not null && AllowedImageContentTypes.Contains( contentType.ToLowerInvariant( ) ) ? image : null;
    }
}
