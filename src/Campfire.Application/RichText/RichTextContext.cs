namespace Campfire.Application.RichText;

/// <summary>Everything rendering needs from the request and the app.</summary>
/// <param name="Resolver">Finds attached records.</param>
/// <param name="RequestHost"><c>Current.request_host</c>: link previews may not point back at this host.</param>
public sealed record RichTextContext( IAttachableResolver Resolver, string? RequestHost );
