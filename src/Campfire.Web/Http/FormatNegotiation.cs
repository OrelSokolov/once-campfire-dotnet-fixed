namespace Campfire.Web.Http;

/// <summary>
/// <c>ActionDispatch::Http::MimeNegotiation</c> (rails <c>1a02651</c>): <c>request.formats</c>,
/// <c>negotiate_mime</c> and <c>should_apply_vary_header?</c>. Ported from Rust
/// <c>crates/kit/src/format.rs</c> at <c>3fe9f61</c>.
/// </summary>
public static class FormatNegotiation
{
    /// <summary><c>request.formats</c>. Throws <see cref="InvalidMimeTypeException"/> for a malformed <c>Accept</c> or <c>Content-Type</c>.</summary>
    public static IReadOnlyList<MimeType> Formats( in NegotiationInput input )
    {
        if ( input.FormatParam is { } format )
        {
            return Mime.LookupByExtension( format ) is { } mime ? [mime] : [];
        }

        if ( ValidAcceptHeader( input ) )
        {
            var accept = (input.Accept ?? "").Trim( );
            if ( accept.Length > 0 )
            {
                return AcceptHeader.Parse( accept );
            }

            return ContentMimeType( input.ContentType ) is { } content ? [content] : [];
        }

        if ( FormatFromPathExtension( input.Path ) is { } extension )
        {
            return [extension];
        }

        return [input.Xhr ? Mime.Js : Mime.Html];
    }

    /// <summary>
    /// <c>request.content_mime_type</c>: the registered type of the <c>Content-Type</c> header, or
    /// <c>null</c> for none or an unregistered type. Throws <see cref="InvalidMimeTypeException"/>.
    /// </summary>
    public static MimeType? ContentMimeType( string? contentType )
    {
        if ( contentType is null )
        {
            return null;
        }

        var end = contentType.AsSpan( ).IndexOfAny( ',', ';' );
        var baseType = (end < 0 ? contentType : contentType[..end]).Trim( ).ToLowerInvariant( );

        return baseType.Length == 0 ? null : Mime.Lookup( baseType );
    }

    /// <summary>
    /// <c>should_apply_vary_header?</c>: <c>!params_readable? &amp;&amp; use_accept_header &amp;&amp;
    /// valid_accept_header</c>, i.e. the format came from the <c>Accept</c> header, not a format param.
    /// </summary>
    public static bool ShouldApplyVaryHeader( in NegotiationInput input ) => input.FormatParam is null && ValidAcceptHeader( input );

    /// <summary><c>request.negotiate_mime(order)</c>: the first acceptable format <paramref name="order"/> offers.</summary>
    public static MimeType? Negotiate( IReadOnlyList<MimeType> formats, IReadOnlyList<MimeType> order )
    {
        ArgumentNullException.ThrowIfNull( formats );
        ArgumentNullException.ThrowIfNull( order );

        foreach ( var priority in formats )
        {
            if ( priority == Mime.All )
            {
                return order.Count > 0 ? order[0] : null;
            }

            if ( order.Contains( priority ) )
            {
                return priority;
            }
        }

        return order.Contains( Mime.All ) && formats.Count > 0 ? formats[0] : null;
    }

    private static bool ValidAcceptHeader( in NegotiationInput input )
    {
        var accept = input.Accept ?? "";
        var present = accept.Trim( ).Length > 0;

        return (input.Xhr && (present || !string.IsNullOrEmpty( input.ContentType ))) || (present && !BrowserLike( accept ));
    }

    /// <summary><c>BROWSER_LIKE_ACCEPTS = /,\s*\*\/\*|\*\/\*\s*,/</c>.</summary>
    private static bool BrowserLike( string accept )
    {
        var compact = string.Concat( accept.Where( character => !AcceptHeader.IsRubySpace( character ) ) );

        return compact.Contains( ",*/*", StringComparison.Ordinal ) || compact.Contains( "*/*,", StringComparison.Ordinal );
    }

    /// <summary><c>path.match(/\.(\w+)\z/)</c> then <c>Mime[ext]</c>.</summary>
    private static MimeType? FormatFromPathExtension( string path )
    {
        var dotIndex = path.LastIndexOf( '.' );
        if ( dotIndex < 0 )
        {
            return null;
        }

        var extension = path[(dotIndex + 1)..];
        if ( extension.Length == 0 || !extension.All( character => char.IsAsciiLetterOrDigit( character ) || character == '_' ) )
        {
            return null;
        }

        return Mime.LookupByExtension( extension );
    }
}
