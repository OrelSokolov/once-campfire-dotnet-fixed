namespace Campfire.Web.Http.Inputs;

/// <summary>A request's parameters: query and form merged (<c>params</c>), and each source on its own.</summary>
public sealed record FrontendFields( InputObject Combined, InputObject Form, InputObject Query );
