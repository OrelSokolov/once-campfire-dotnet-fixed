namespace Campfire.Web.Http;

/// <summary>Everything <c>MimeNegotiation#formats</c> looks at.</summary>
/// <param name="FormatParam">
/// <c>params[:format]</c> (the path extension the router captured, or <c>?format=</c>); <c>null</c>
/// when absent. A non-string value is passed as its JSON text, which names no type.
/// </param>
/// <param name="Accept">The <c>Accept</c> header.</param>
/// <param name="ContentType">The <c>Content-Type</c> header.</param>
/// <param name="Path">The request path (<c>PATH_INFO</c>), for the extension fallback.</param>
/// <param name="Xhr"><c>request.xhr?</c>.</param>
public readonly record struct NegotiationInput( string? FormatParam, string? Accept, string? ContentType, string Path, bool Xhr );
